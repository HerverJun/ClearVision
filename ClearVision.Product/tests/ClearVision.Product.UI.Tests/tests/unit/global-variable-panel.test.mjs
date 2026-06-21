import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { chromium } from 'playwright';

const PRODUCTION_CSS_FILES = [
  'src/shared/styles/variables.css',
  'src/shared/styles/main.css',
  'src/shared/styles/ui-components.css',
  'src/shared/styles/settings.css',
  'src/shared/styles/property-panel.css',
  'src/shared/styles/global-variables.css',
  'src/shared/styles/property-panel-enhancements.css',
  'src/shared/styles/project-view.css',
  'src/shared/styles/global-enhancements.css',
  'src/shared/styles/settings-view-override.css',
  'src/shared/styles/operator-library.css',
  'src/features/settings/userManagement.css',
  'src/shared/styles/ai-panel.css',
  'src/shared/styles/sprint-c-enhancements.css',
  'src/shared/styles/analysisCards.css',
  'src/shared/styles/inspection.css',
  'src/shared/styles/results.css',
  'src/shared/styles/stations.css',
  'src/shared/styles/visual-upgrade.css',
  'css/planarScaleOffsetCalibWizard.css'
];

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

  assert.deepEqual(store.coerceGlobalVariableValue('Int64', '9007199254740991'), {
    ok: true,
    value: 9007199254740991,
    error: ''
  });
  assert.deepEqual(store.coerceGlobalVariableValue('Int64', '-9007199254740991'), {
    ok: true,
    value: -9007199254740991,
    error: ''
  });
  assert.equal(store.coerceGlobalVariableValue('Int64', '9007199254740992').ok, false);
  assert.match(store.coerceGlobalVariableValue('Int64', '9.007199254740992e15').error, /超出前端安全整数范围/);
  assert.equal(store.coerceGlobalVariableValue('Double', '9.007199254740992e15').value, 9007199254740992);
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
    getRuntimeState: () => ({ projectId: project.id, status: 'Running' }),
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

function clone(value) {
  return JSON.parse(JSON.stringify(value));
}

function createInteractiveProject({ single = false } = {}) {
  const project = createProject();
  project.flow.operators[0].outputPorts.push({ id: 'out-alt', name: 'AltCount', dataType: 'Integer' });
  project.globalVariables.sourceBindings.push({
    id: 'source-count',
    variableId: 'var-count',
    operatorId: 'op-counter',
    outputPortId: 'out-count',
    operatorName: 'Counter',
    outputPortName: 'Count'
  });
  project.globalVariables.targetBindings.push({
    id: 'target-count',
    variableId: 'var-count',
    operatorId: 'op-counter',
    parameterId: 'param-threshold',
    operatorName: 'Counter',
    parameterName: 'Threshold'
  });
  if (single) {
    project.globalVariables.sourceBindings = [];
    project.globalVariables.targetBindings = [];
  } else {
    project.globalVariables.variables.push({
      id: 'var-temp',
      name: 'temp.value',
      displayName: 'Temp',
      description: '',
      valueType: 'String',
      initialValue: 'x',
      min: null,
      max: null,
      manualWriteAllowed: true,
      includeInResultMetadata: false,
      order: 2
    });
  }
  return project;
}

async function startBrowserHarness(initialState = {}) {
  const root = path.resolve(process.cwd(), '../../..');
  const wwwroot = path.join(root, 'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot');
  const state = {
    values: [{ variableId: 'var-count', value: 4, version: 1 }],
    savedSchemas: [],
    failSave: false,
    failValues: false,
    failWrite: false,
    failResetOne: false,
    failResetAll: false,
    conflictSave: false,
    runtimeStateCalls: 0,
    runtimeStates: [{ status: 'Idle', isBusy: false }],
    ...initialState
  };

  async function readBody(request) {
    const chunks = [];
    for await (const chunk of request) {
      chunks.push(chunk);
    }
    const text = Buffer.concat(chunks).toString('utf8');
    return text ? JSON.parse(text) : null;
  }

  function sendJson(response, status, payload) {
    response.writeHead(status, { 'content-type': 'application/json' });
    response.end(JSON.stringify(payload));
  }

  const server = http.createServer(async (request, response) => {
    const url = new URL(request.url, 'http://localhost');
    if (/^\/api\/inspection\/realtime\/[^/]+\/state$/.test(url.pathname) && request.method === 'GET') {
      const index = Math.min(state.runtimeStateCalls, state.runtimeStates.length - 1);
      state.runtimeStateCalls += 1;
      return sendJson(response, 200, {
        projectId: url.pathname.split('/').at(-2),
        sessionId: 'session-runtime',
        startedAt: '2026-06-20T00:00:00Z',
        stoppedAt: state.runtimeStates[index]?.status === 'Stopped' || state.runtimeStates[index]?.status === 'Faulted'
          ? '2026-06-20T00:00:01Z'
          : null,
        ...state.runtimeStates[index]
      });
    }
    if (url.pathname.startsWith('/api/projects/')) {
      if (url.pathname.endsWith('/global-variable-values') && request.method === 'GET') {
        return state.failValues ? sendJson(response, 500, { error: 'values failed' }) : sendJson(response, 200, state.values);
      }
      if (url.pathname.endsWith('/global-variables') && request.method === 'PUT') {
        const body = await readBody(request);
        state.savedSchemas.push(body);
        if (state.conflictSave) {
          return sendJson(response, 409, { error: 'Project is currently running.' });
        }
        return state.failSave ? sendJson(response, 500, { error: 'save failed' }) : sendJson(response, 200, body);
      }
      if (url.pathname.includes('/global-variable-values/') && url.pathname.endsWith('/reset') && request.method === 'POST') {
        return state.failResetOne ? sendJson(response, 500, { error: 'reset one failed' }) : sendJson(response, 200, state.values);
      }
      if (url.pathname.endsWith('/global-variable-values/reset') && request.method === 'POST') {
        return state.failResetAll ? sendJson(response, 500, { error: 'reset all failed' }) : sendJson(response, 200, state.values);
      }
      if (url.pathname.includes('/global-variable-values/') && request.method === 'PUT') {
        await readBody(request);
        return state.failWrite ? sendJson(response, 500, { error: 'write failed' }) : sendJson(response, 200, state.values);
      }
    }

    if (url.pathname === '/' || url.pathname === '/harness.html') {
      response.writeHead(200, { 'content-type': 'text/html' });
      response.end(`<!doctype html>
<html lang="zh-CN" data-theme="dark">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <script>window.__API_BASE_URL__=location.origin+"/api";</script>
  ${PRODUCTION_CSS_FILES.map(file => `<link rel="stylesheet" href="/${file}">`).join('\n  ')}
  <style>
    body { margin: 0; width: 100vw; height: 100vh; overflow: hidden; background: var(--workspace-bg); }
    body::before { content: ""; position: fixed; inset: 0; background-image: radial-gradient(circle at 1px 1px, rgba(100,116,139,.28) 1px, transparent 0); background-size: 20px 20px; }
    #global-variables-root { position: fixed; right: 24px; top: 56px; width: 260px; z-index: 10; }
  </style>
</head>
<body>
  <div id="global-variables-root"></div>
  <div id="property-root"></div>
</body>
</html>`);
      return;
    }

    const relativePath = decodeURIComponent(url.pathname.replace(/^\/+/, ''));
    const filePath = path.resolve(wwwroot, relativePath);
    if (!filePath.startsWith(wwwroot) || !fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
      response.writeHead(404);
      response.end('not found');
      return;
    }
    const type = filePath.endsWith('.js') || filePath.endsWith('.mjs')
      ? 'text/javascript'
      : (filePath.endsWith('.css') ? 'text/css' : 'text/plain');
    response.writeHead(200, { 'content-type': type });
    fs.createReadStream(filePath).pipe(response);
  });

  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const baseUrl = `http://127.0.0.1:${server.address().port}`;
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage();
  await page.goto(`${baseUrl}/harness.html`);
  return {
    page,
    state,
    async close() {
      await browser.close();
      await new Promise(resolve => server.close(resolve));
    }
  };
}

async function setupGlobalVariablePanel(page, project, { choices = [], runtime = false } = {}) {
  await page.evaluate(async ({ project, choices, runtime }) => {
    window.__panel?.destroy?.();
    document.querySelectorAll('.gv-manager-overlay, .gv-choice-overlay').forEach(node => node.remove());
    const host = document.getElementById('global-variables-root');
    if (host) {
      host.innerHTML = '';
    }
    const [panelModule, projectModule, serviceModule] = await Promise.all([
      import('/src/features/global-variables/globalVariablePanel.js'),
      import('/src/features/project/projectManager.js'),
      import('/src/core/app/serviceRegistry.js')
    ]);
    const projectManager = projectModule.default;
    projectManager.currentProject = structuredClone(project);
    window.__projectManager = projectManager;
    window.__choices = [...choices];
    window.__runtimeSubscribers = [];
    window.__toasts = [];
    const serviceRegistry = serviceModule.default;
    serviceRegistry.register('flowCanvas', { schemas: [], setGlobalVariableSchema(schema) { this.schemas.push(structuredClone(schema)); } });
    serviceRegistry.register('propertyPanel', { renders: 0, render() { this.renders += 1; } });
    const options = {
      requestChoice() {
        return window.__choices.length ? window.__choices.shift() : 'cancel';
      },
      showToast(message, type) {
        window.__toasts.push({ message, type });
      }
    };
    if (runtime) {
      options.subscribeRuntimeState = callback => {
        window.__runtimeSubscribers.push(callback);
        callback({ projectId: project.id, status: 'idle', isRunning: false, isRealtime: false });
        return () => {
          window.__runtimeSubscribers = window.__runtimeSubscribers.filter(item => item !== callback);
        };
      };
    }
    const panel = new panelModule.default('global-variables-root', options);
    window.__panel = panel;
    await panel.setProject(projectManager.currentProject);
    panel.openManager();
  }, { project, choices, runtime });
}

async function getBrowserPanelState(page) {
  return await page.evaluate(() => ({
    selectedVariableId: window.__panel.selectedVariableId,
    draft: structuredClone(window.__panel.draft),
    schema: structuredClone(window.__panel.schema),
    baselineSchema: structuredClone(window.__panel.baselineSchema),
    values: structuredClone(window.__panel.values),
    dirty: window.__panel.dirty,
    locked: window.__panel.isRuntimeLocked(),
    projectSchema: structuredClone(window.__projectManager.currentProject.globalVariables),
    activeId: document.activeElement?.id || '',
    searchValue: document.querySelector('#gv-search')?.value || '',
    searchSelectionStart: document.querySelector('#gv-search')?.selectionStart ?? -1
  }));
}

async function getFieldErrorText(page, field) {
  return await page.evaluate(fieldName =>
    document.querySelector(`[data-field="${fieldName}"]`)
      ?.closest('.gv-field')
      ?.querySelector('.gv-field-error')
      ?.textContent
      ?.trim() || '', field);
}

function alphaFromCssColor(value) {
  const match = String(value || '').match(/rgba?\(([^)]+)\)/i);
  if (!match) {
    return 1;
  }
  const parts = match[1].split(',').map(part => part.trim());
  return parts.length >= 4 ? Number(parts[3]) : 1;
}

function overlaps(a, b) {
  if (!a || !b) {
    return false;
  }
  return a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top;
}

async function getVisualMetrics(page) {
  return await page.evaluate(() => {
    const rectOf = selector => {
      const node = document.querySelector(selector);
      if (!node) return null;
      const rect = node.getBoundingClientRect();
      return {
        left: rect.left,
        top: rect.top,
        right: rect.right,
        bottom: rect.bottom,
        width: rect.width,
        height: rect.height
      };
    };
    const manager = document.querySelector('.gv-manager');
    const detail = document.querySelector('.gv-detail');
    const save = document.querySelector('[data-action="save"]');
    const newButton = document.querySelector('[data-action="new"]');
    return {
      viewport: { width: innerWidth, height: innerHeight },
      manager: rectOf('.gv-manager'),
      header: rectOf('.gv-manager-header'),
      toolbar: rectOf('.gv-toolbar'),
      list: rectOf('.gv-variable-list'),
      detail: rectOf('.gv-detail'),
      managerBackground: getComputedStyle(manager).backgroundColor,
      managerClientWidth: manager?.clientWidth ?? 0,
      managerScrollWidth: manager?.scrollWidth ?? 0,
      detailClientWidth: detail?.clientWidth ?? 0,
      detailScrollWidth: detail?.scrollWidth ?? 0,
      detailClientHeight: detail?.clientHeight ?? 0,
      detailScrollHeight: detail?.scrollHeight ?? 0,
      emptyCount: document.querySelectorAll('.gv-empty').length,
      newButtonCount: document.querySelectorAll('[data-action="new"]').length,
      variableRows: document.querySelectorAll('.gv-variable-row').length,
      detailEmpty: document.querySelector('.gv-detail')?.classList.contains('gv-detail-empty') ?? false,
      lockedReason: document.querySelector('.gv-empty-detail .gv-muted')?.textContent?.trim() || '',
      emptyCard: rectOf('.gv-empty-detail'),
      saveButtons: [...document.querySelectorAll('[data-action="save"]')].map(button => ({ disabled: button.disabled })),
      resetAllDisabled: document.querySelector('[data-action="reset-all"]')?.disabled ?? null,
      newButton: newButton ? {
        disabled: newButton.disabled,
        rect: (() => {
          const rect = newButton.getBoundingClientRect();
          return { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height };
        })()
      } : null,
      saveButton: save ? {
        disabled: save.disabled,
        rect: (() => {
          const rect = save.getBoundingClientRect();
          return { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height };
        })()
      } : null
    };
  });
}

test('browser interaction keeps original draft, schema and selection when new variable is cancelled', async () => {
  const harness = await startBrowserHarness();
  try {
    const project = createInteractiveProject();
    await setupGlobalVariablePanel(harness.page, project, { choices: ['cancel'] });
    await harness.page.fill('input[data-field="displayName"]', 'Edited Count');
    await harness.page.click('[data-action="new"]');
    const state = await getBrowserPanelState(harness.page);
    assert.equal(state.selectedVariableId, 'var-count');
    assert.equal(state.draft.displayName, 'Edited Count');
    assert.equal(state.schema.variables.find(item => item.id === 'var-count').displayName, project.globalVariables.variables[0].displayName);
    assert.equal(state.dirty, true);
  } finally {
    await harness.close();
  }
});

test('browser visual layout uses production CSS and keeps modal opaque inside viewport', async () => {
  const harness = await startBrowserHarness();
  try {
    const project = createInteractiveProject();
    const viewports = [
      { width: 1564, height: 314 },
      { width: 1366, height: 768 },
      { width: 1024, height: 600 }
    ];
    for (const viewport of viewports) {
      for (const theme of ['light', 'dark']) {
        await harness.page.setViewportSize(viewport);
        await setupGlobalVariablePanel(harness.page, project);
        await harness.page.evaluate(themeName => {
          document.documentElement.dataset.theme = themeName;
          document.documentElement.style.colorScheme = themeName;
        }, theme);
        const metrics = await getVisualMetrics(harness.page);
        assert.equal(alphaFromCssColor(metrics.managerBackground), 1);
        assert.ok(metrics.manager.left >= 0);
        assert.ok(metrics.manager.top >= 0);
        assert.ok(metrics.manager.right <= metrics.viewport.width);
        assert.ok(metrics.manager.bottom <= metrics.viewport.height);
        assert.equal(overlaps(metrics.header, metrics.toolbar), false);
        assert.equal(overlaps(metrics.toolbar, metrics.list), false);
        assert.equal(overlaps(metrics.toolbar, metrics.detail), false);
        assert.ok(metrics.managerScrollWidth <= metrics.managerClientWidth + 1);
        assert.ok(metrics.detailScrollWidth <= metrics.detailClientWidth + 1);
        if (viewport.height === 314) {
          assert.ok(metrics.detailScrollHeight > metrics.detailClientHeight);
          assert.ok(metrics.saveButton.rect.top >= 0 && metrics.saveButton.rect.bottom <= metrics.viewport.height);
        }
      }
    }
  } finally {
    await harness.close();
  }
});

test('browser visual layout distinguishes zero variables from search without results', async () => {
  const harness = await startBrowserHarness();
  try {
    await harness.page.setViewportSize({ width: 1564, height: 314 });
    const emptyProject = createInteractiveProject({ single: true });
    emptyProject.globalVariables.variables = [];
    emptyProject.globalVariables.sourceBindings = [];
    emptyProject.globalVariables.targetBindings = [];
    await setupGlobalVariablePanel(harness.page, emptyProject);
    let metrics = await getVisualMetrics(harness.page);
    assert.equal(metrics.emptyCount, 1);
    assert.equal(metrics.newButtonCount, 1);
    assert.equal(metrics.detailEmpty, true);
    assert.equal(metrics.variableRows, 0);
    assert.equal(metrics.saveButtons.length, 0);
    assert.equal(metrics.resetAllDisabled, true);
    assert.ok(metrics.newButton);
    assert.equal(metrics.newButton.disabled, false);
    assert.ok(metrics.emptyCard.left >= metrics.detail.left);
    assert.ok(metrics.emptyCard.right <= metrics.detail.right);
    assert.ok(metrics.emptyCard.top >= metrics.detail.top);
    assert.ok(metrics.emptyCard.bottom <= metrics.detail.bottom);
    assert.ok(metrics.newButton.rect.top >= 0 && metrics.newButton.rect.bottom <= metrics.viewport.height);

    await harness.page.click('[data-action="new"]');
    metrics = await getVisualMetrics(harness.page);
    assert.equal(metrics.detailEmpty, false);
    assert.equal(metrics.newButtonCount, 1);
    assert.equal(metrics.saveButtons.length, 1);
    assert.equal((await getBrowserPanelState(harness.page)).draft.isNew, true);

    await setupGlobalVariablePanel(harness.page, emptyProject, { runtime: true });
    await harness.page.evaluate(() => {
      window.__runtimeSubscribers.forEach(callback => callback({
        projectId: window.__panel.project.id,
        sessionId: 'empty-running',
        status: 'running',
        isRunning: true,
        isRealtime: true
      }));
    });
    metrics = await getVisualMetrics(harness.page);
    assert.equal(metrics.newButtonCount, 1);
    assert.equal(metrics.newButton.disabled, true);
    assert.match(metrics.lockedReason, /工程运行中/);

    await setupGlobalVariablePanel(harness.page, createInteractiveProject());
    await harness.page.fill('#gv-search', 'no-matching-variable');
    metrics = await getVisualMetrics(harness.page);
    assert.equal(metrics.variableRows, 0);
    assert.equal(metrics.emptyCount, 1);
    assert.equal(metrics.newButtonCount, 1);
    assert.equal(metrics.detailEmpty, false);
    assert.equal(metrics.saveButtons.length, 1);
  } finally {
    await harness.close();
  }
});

test('browser interaction preserves draft and dirty state after save, refresh, write and reset failures', async () => {
  const harness = await startBrowserHarness();
  try {
    const project = createInteractiveProject({ single: true });
    await setupGlobalVariablePanel(harness.page, project, { choices: ['reset'] });
    await harness.page.fill('input[data-field="displayName"]', 'Failure Draft');

    harness.state.failSave = true;
    await harness.page.click('[data-action="save"]');
    await harness.page.waitForFunction(() => window.__panel.errorMessage);

    harness.state.failValues = true;
    await harness.page.click('.gv-toolbar [data-action="refresh"]');
    await harness.page.waitForFunction(() => window.__panel.pendingAction === '');

    harness.state.failValues = false;
    harness.state.failWrite = true;
    await harness.page.fill('#gv-write-value', '5');
    await harness.page.click('[data-action="write"]');
    await harness.page.waitForFunction(() => window.__panel.pendingAction === '');

    harness.state.failResetOne = true;
    await harness.page.click('[data-action="reset-one"]');
    await harness.page.waitForFunction(() => window.__panel.pendingAction === '');

    harness.state.failResetAll = true;
    await harness.page.click('.gv-toolbar [data-action="reset-all"]');
    await harness.page.waitForFunction(() => window.__panel.pendingAction === '');

    const state = await getBrowserPanelState(harness.page);
    assert.equal(state.draft.displayName, 'Failure Draft');
    assert.equal(state.dirty, true);
    assert.equal(state.schema.variables[0].displayName, project.globalVariables.variables[0].displayName);
  } finally {
    await harness.close();
  }
});

test('browser interaction restores deleted variables, source changes and removed targets on discard', async () => {
  const harness = await startBrowserHarness();
  try {
    const project = createInteractiveProject();
    const expected = clone(project.globalVariables);
    await setupGlobalVariablePanel(harness.page, project, { choices: ['delete'] });

    await harness.page.click('[data-variable-id="var-temp"]');
    await harness.page.click('[data-action="delete"]');
    await harness.page.click('[data-action="source-dialog"]');
    await harness.page.click('.gv-source-option[data-output-port-id="out-alt"]');
    await harness.page.click('[data-action="remove-target"]');
    await harness.page.click('[data-action="discard"]');

    const state = await getBrowserPanelState(harness.page);
    assert.deepEqual(state.schema, expected);
    assert.deepEqual(state.projectSchema, expected);
    assert.equal(state.dirty, false);
  } finally {
    await harness.close();
  }
});

test('browser interaction deletes the last variable and saves an empty schema', async () => {
  const harness = await startBrowserHarness();
  try {
    const project = createInteractiveProject({ single: true });
    await setupGlobalVariablePanel(harness.page, project, { choices: ['delete'] });

    await harness.page.click('[data-action="delete"]');
    await harness.page.click('[data-action="save"]');
    await harness.page.waitForFunction(() => window.__panel.dirty === false);

    const state = await getBrowserPanelState(harness.page);
    assert.equal(harness.state.savedSchemas.length, 1);
    assert.deepEqual(harness.state.savedSchemas[0].variables, []);
    assert.deepEqual(state.baselineSchema.variables, []);
    assert.deepEqual(state.projectSchema.variables, []);
  } finally {
    await harness.close();
  }
});

test('browser interaction renders search and filters with clean Chinese list markup', async () => {
  const harness = await startBrowserHarness();
  try {
    const project = createInteractiveProject();
    await setupGlobalVariablePanel(harness.page, project);

    const root = path.resolve(process.cwd(), '../../..');
    const panelSource = fs.readFileSync(path.join(root, 'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/global-variables/globalVariablePanel.js'), 'utf8');
    assert.doesNotMatch(panelSource, /\?\/div>| 路 |姝ｅ湪鍔犺浇|娌℃湁绗﹀悎/);
    assert.equal((panelSource.match(/filteredVariables\.map\(variable =>/g) || []).length, 1);

    let list = await harness.page.locator('.gv-variable-list').innerHTML();
    assert.match(list, /judge\.expected_count · 整数/);
    assert.doesNotMatch(list, /\?\/div>| 路 |姝ｅ湪鍔犺浇|娌℃湁绗﹀悎/);
    assert.equal(await harness.page.locator('.gv-variable-row').count(), 2);

    await harness.page.fill('#gv-search', 'expected');
    assert.equal(await harness.page.locator('.gv-variable-row').count(), 1);
    assert.match(await harness.page.locator('.gv-variable-list').textContent(), /judge\.expected_count · 整数/);

    await harness.page.fill('#gv-search', 'missing');
    assert.equal(await harness.page.locator('.gv-variable-row').count(), 0);
    assert.match(await harness.page.locator('.gv-variable-list').textContent(), /没有符合条件的变量。/);
    list = await harness.page.locator('.gv-variable-list').innerHTML();
    assert.doesNotMatch(list, /\?\/div>| 路 /);

    await harness.page.fill('#gv-search', '');
    await harness.page.selectOption('#gv-type-filter', { label: '文本' });
    assert.equal(await harness.page.locator('.gv-variable-row').count(), 1);
    assert.match(await harness.page.locator('.gv-variable-list').textContent(), /temp\.value · 文本/);

    await harness.page.selectOption('#gv-type-filter', { label: '全部类型' });
    await harness.page.selectOption('#gv-source-filter', { label: '算子输出' });
    assert.equal(await harness.page.locator('.gv-variable-row').count(), 1);
    assert.match(await harness.page.locator('.gv-variable-list').textContent(), /judge\.expected_count · 整数/);

    await harness.page.selectOption('#gv-source-filter', { label: '固定初始值' });
    assert.equal(await harness.page.locator('.gv-variable-row').count(), 1);
    assert.match(await harness.page.locator('.gv-variable-list').textContent(), /temp\.value · 文本/);

    const state = await getBrowserPanelState(harness.page);
    assert.equal(state.selectedVariableId, 'var-count');
    assert.equal(state.draft.id, 'var-count');
  } finally {
    await harness.close();
  }
});

test('browser interaction keeps search focus and toggles runtime readonly state without dropping draft', async () => {
  const harness = await startBrowserHarness();
  try {
    const project = createInteractiveProject();
    await setupGlobalVariablePanel(harness.page, project, { runtime: true });

    await harness.page.focus('#gv-search');
    await harness.page.keyboard.type('exp');
    let state = await getBrowserPanelState(harness.page);
    assert.equal(state.activeId, 'gv-search');
    assert.equal(state.searchValue, 'exp');
    assert.equal(state.searchSelectionStart, 3);
    assert.equal(state.selectedVariableId, 'var-count');

    await harness.page.fill('input[data-field="displayName"]', 'Runtime Draft');
    await harness.page.evaluate(() => {
      window.__runtimeSubscribers.forEach(callback => callback({ projectId: window.__panel.project.id, sessionId: 'runtime-draft-session', status: 'running', isRunning: true, isRealtime: false }));
    });
    await harness.page.waitForFunction(() => document.querySelector('input[data-field="displayName"]')?.disabled === true);
    state = await getBrowserPanelState(harness.page);
    assert.equal(state.draft.displayName, 'Runtime Draft');
    assert.equal(state.locked, true);

    await harness.page.evaluate(() => {
      window.__runtimeSubscribers.forEach(callback => callback({ projectId: window.__panel.project.id, status: 'idle', isRunning: false, isRealtime: false }));
    });
    await harness.page.waitForFunction(() => document.querySelector('input[data-field="displayName"]')?.disabled === false);
    state = await getBrowserPanelState(harness.page);
    assert.equal(state.draft.displayName, 'Runtime Draft');
    assert.equal(state.locked, false);
  } finally {
    await harness.close();
  }
});

test('browser interaction recovers from a 409 using real runtime state polling without dropping draft', async () => {
  const harness = await startBrowserHarness({
    conflictSave: true,
    values: [{ variableId: 'var-count', value: 9, version: 9 }],
    runtimeStates: [
      { status: 'Running', isBusy: true },
      { status: 'Stopped', isBusy: false }
    ]
  });
  try {
    const project = createInteractiveProject({ single: true });
    await setupGlobalVariablePanel(harness.page, project);

    await harness.page.fill('input[data-field="displayName"]', 'Conflict Draft');
    await harness.page.click('[data-action="save"]');
    await harness.page.waitForFunction(() => document.querySelector('input[data-field="displayName"]')?.disabled === true);
    await harness.page.waitForFunction(() => document.querySelector('input[data-field="displayName"]')?.disabled === false, { timeout: 5000 });

    const state = await getBrowserPanelState(harness.page);
    assert.equal(state.draft.displayName, 'Conflict Draft');
    assert.equal(state.dirty, true);
    assert.equal(state.values[0].version, 9);
    assert.equal(state.locked, false);
    assert.equal(harness.state.runtimeStateCalls, 2);
  } finally {
    await harness.close();
  }
});

test('browser interaction treats project runtime endpoint as authority over stale local running state', async () => {
  const harness = await startBrowserHarness({
    runtimeStates: [
      { sessionId: 'old-session', status: 'Stopped', isBusy: false }
    ]
  });
  try {
    const project = createInteractiveProject({ single: true });
    await setupGlobalVariablePanel(harness.page, project, { runtime: true });
    await harness.page.evaluate(() => {
      window.__runtimeSubscribers.forEach(callback => callback({
        projectId: window.__panel.project.id,
        sessionId: 'old-session',
        status: 'running',
        isRunning: true,
        isRealtime: true
      }));
    });
    await harness.page.waitForFunction(() => window.__panel.isRuntimeLocked() === true);
    await harness.page.evaluate(async () => {
      await window.__panel.syncRuntimeStateAfterConflict(window.__panel.project.id, { schedulePoll: true });
    });
    await harness.page.waitForFunction(() => window.__panel.isRuntimeLocked() === false);
    const state = await getBrowserPanelState(harness.page);
    assert.equal(state.locked, false);
    assert.equal(harness.state.runtimeStateCalls, 1);

    await harness.page.evaluate(() => {
      window.__runtimeSubscribers.forEach(callback => callback({
        projectId: window.__panel.project.id,
        sessionId: 'old-session',
        status: 'running',
        isRunning: true,
        isRealtime: true
      }));
    });
    assert.equal(await harness.page.evaluate(() => window.__panel.isRuntimeLocked()), false);

    await harness.page.evaluate(() => {
      window.__runtimeSubscribers.forEach(callback => callback({
        projectId: window.__panel.project.id,
        sessionId: 'new-session',
        status: 'starting',
        isRunning: true,
        isRealtime: true
      }));
    });
    await harness.page.waitForFunction(() => window.__panel.isRuntimeLocked() === true);
    assert.equal((await getBrowserPanelState(harness.page)).locked, true);
  } finally {
    await harness.close();
  }
});

test('browser interaction confirms sessionless busy events with the runtime endpoint', async () => {
  const harness = await startBrowserHarness({
    runtimeStates: [
      { sessionId: 'old-session', status: 'Stopped', isBusy: false },
      { sessionId: 'new-session', status: 'Running', isBusy: true }
    ]
  });
  try {
    const project = createInteractiveProject({ single: true });
    await setupGlobalVariablePanel(harness.page, project, { runtime: true });
    await harness.page.evaluate(async () => {
      await window.__panel.syncRuntimeStateAfterConflict(window.__panel.project.id, { schedulePoll: true });
    });
    await harness.page.waitForFunction(() => window.__panel.isRuntimeLocked() === false);

    await harness.page.evaluate(() => {
      window.__runtimeSubscribers.forEach(callback => callback({
        projectId: window.__panel.project.id,
        status: 'running',
        isRunning: true,
        isRealtime: true
      }));
    });

    await harness.page.waitForFunction(() => window.__panel.runtimeState?.sessionId === 'new-session');
    const state = await getBrowserPanelState(harness.page);
    assert.equal(state.locked, true);
    assert.equal(harness.state.runtimeStateCalls, 2);
  } finally {
    await harness.close();
  }
});

test('browser interaction ignores running state from previous project after switching to an idle project', async () => {
  const harness = await startBrowserHarness();
  try {
    const projectA = createInteractiveProject({ single: true });
    await setupGlobalVariablePanel(harness.page, projectA, { runtime: true });
    await harness.page.evaluate(() => {
      window.__runtimeSubscribers.forEach(callback => callback({
        projectId: window.__panel.project.id,
        sessionId: 'project-a-session',
        status: 'running',
        isRunning: true,
        isRealtime: true
      }));
    });
    await harness.page.waitForFunction(() => window.__panel.isRuntimeLocked() === true);

    const projectB = createInteractiveProject({ single: true });
    projectB.id = 'project-b-idle';
    await harness.page.evaluate(async nextProject => {
      await window.__panel.setProject(nextProject);
    }, projectB);

    const state = await getBrowserPanelState(harness.page);
    assert.equal(state.locked, false);
    assert.equal(state.projectSchema.variables.length, 1);
  } finally {
    await harness.close();
  }
});

test('browser interaction cancels runtime state polling on project switch and destroy', async () => {
  const harness = await startBrowserHarness({
    conflictSave: true,
    runtimeStates: [
      { status: 'Running', isBusy: true },
      { status: 'Running', isBusy: true },
      { status: 'Running', isBusy: true }
    ]
  });
  try {
    const project = createInteractiveProject({ single: true });
    await setupGlobalVariablePanel(harness.page, project);

    await harness.page.fill('input[data-field="displayName"]', 'Switch Draft');
    await harness.page.click('[data-action="save"]');
    await harness.page.waitForFunction(() => window.__panel.runtimeStatePollTimer !== null);
    assert.equal(harness.state.runtimeStateCalls, 1);

    await harness.page.evaluate(async project => {
      const nextProject = structuredClone(project);
      nextProject.id = 'project-next';
      await window.__panel.setProject(nextProject);
    }, project);
    await harness.page.waitForTimeout(1800);
    assert.equal(harness.state.runtimeStateCalls, 1);

    await harness.page.fill('input[data-field="displayName"]', 'Destroy Draft');
    await harness.page.click('[data-action="save"]');
    await harness.page.waitForFunction(() => window.__panel.runtimeStatePollTimer !== null);
    assert.equal(harness.state.runtimeStateCalls, 2);

    await harness.page.evaluate(() => window.__panel.destroy());
    await harness.page.waitForTimeout(1800);
    assert.equal(harness.state.runtimeStateCalls, 2);
  } finally {
    await harness.close();
  }
});

test('browser interaction refreshes values and locks after a 409 without dropping draft', async () => {
  const harness = await startBrowserHarness({
    values: [{ variableId: 'var-count', value: 9, version: 9 }],
    runtimeStates: [{ status: 'Running', isBusy: true }]
  });
  try {
    const project = createInteractiveProject({ single: true });
    await setupGlobalVariablePanel(harness.page, project);
    harness.state.conflictSave = true;

    await harness.page.fill('input[data-field="displayName"]', 'Conflict Draft');
    await harness.page.click('[data-action="save"]');
    await harness.page.waitForFunction(() => window.__panel.values.some(item => item.version === 9));

    const state = await getBrowserPanelState(harness.page);
    assert.equal(state.draft.displayName, 'Conflict Draft');
    assert.equal(state.dirty, true);
    assert.equal(state.values[0].version, 9);
    assert.equal(state.locked, true);
  } finally {
    await harness.close();
  }
});

test('browser interaction maps Min and Max validation errors to the matching fields', async () => {
  const harness = await startBrowserHarness();
  try {
    const project = createInteractiveProject({ single: true });
    await setupGlobalVariablePanel(harness.page, project);

    await harness.page.fill('input[data-field="minText"]', 'abc');
    await harness.page.fill('input[data-field="maxText"]', '9007199254740992');
    await harness.page.click('[data-action="save"]');
    await harness.page.waitForFunction(() => Boolean(window.__panel.fieldErrors.min && window.__panel.fieldErrors.max));

    assert.match(await getFieldErrorText(harness.page, 'minText'), /请输入整数。/);
    assert.match(await getFieldErrorText(harness.page, 'maxText'), /超出前端安全整数范围/);

    await harness.page.fill('input[data-field="minText"]', '0');
    assert.equal(await getFieldErrorText(harness.page, 'minText'), '');
    assert.match(await getFieldErrorText(harness.page, 'maxText'), /超出前端安全整数范围/);

    await harness.page.fill('input[data-field="maxText"]', '-1');
    await harness.page.click('[data-action="save"]');
    await harness.page.waitForFunction(() => /最大值/.test(window.__panel.fieldErrors.max || ''));
    assert.match(await getFieldErrorText(harness.page, 'maxText'), /最大值必须大于或等于最小值。/);

    await harness.page.fill('input[data-field="maxText"]', '10');
    assert.equal(await getFieldErrorText(harness.page, 'maxText'), '');
    assert.match(await harness.page.evaluate(() => window.__panel.fieldErrors.initialValue || ''), /初始值不能大于最大值。/);

    await harness.page.click('[data-action="save"]');
    await harness.page.waitForFunction(() => window.__panel.dirty === false);
    assert.deepEqual(await harness.page.evaluate(() => window.__panel.fieldErrors), {});
  } finally {
    await harness.close();
  }
});

test('browser interaction syncs PropertyPanel global-variable binding change and disables fixed input', async () => {
  const harness = await startBrowserHarness();
  try {
    const project = createInteractiveProject();
    project.globalVariables.targetBindings = [];
    await setupGlobalVariablePanel(harness.page, project);
    await harness.page.evaluate(async project => {
      const { PropertyPanel } = await import('/src/features/flow-editor/propertyPanel.js');
      const serviceRegistry = (await import('/src/core/app/serviceRegistry.js')).default;
      window.__externalSchema = null;
      serviceRegistry.register('globalVariablePanel', {
        setSchemaFromExternal(schema) {
          window.__externalSchema = structuredClone(schema);
        }
      });
      const panel = new PropertyPanel('property-root', {});
      panel.showToast = () => {};
      panel.setOperator(structuredClone(project.flow.operators[0]));
      window.__propertyPanel = panel;
    }, project);

    await harness.page.selectOption('.gv-binding-select', 'var-count');
    await harness.page.waitForFunction(() => window.__externalSchema?.targetBindings?.length === 1);

    const result = await harness.page.evaluate(() => {
      const input = document.querySelector('[name="Threshold"]');
      const select = document.querySelector('.gv-binding-select');
      return {
        binding: window.__projectManager.currentProject.globalVariables.targetBindings[0],
        externalBinding: window.__externalSchema.targetBindings[0],
        inputDisabled: input.disabled,
        inputTitle: input.title,
        ariaDisabled: input.getAttribute('aria-disabled'),
        selected: select.value
      };
    });
    assert.equal(result.selected, 'var-count');
    assert.equal(result.binding.variableId, 'var-count');
    assert.equal(result.externalBinding.variableId, 'var-count');
    assert.equal(result.inputDisabled, true);
    assert.ok(result.inputTitle.length > 0);
    assert.equal(result.ariaDisabled, 'true');
  } finally {
    await harness.close();
  }
});

test('browser interaction validates Int64 safe integer boundaries before saving', async () => {
  const harness = await startBrowserHarness();
  try {
    const project = createInteractiveProject({ single: true });
    await setupGlobalVariablePanel(harness.page, project);

    await harness.page.fill('input[data-field="initialValueText"]', '9007199254740991');
    await harness.page.fill('input[data-field="minText"]', '-9007199254740991');
    await harness.page.fill('input[data-field="maxText"]', '9007199254740991');
    await harness.page.click('[data-action="save"]');
    await harness.page.waitForFunction(() => window.__panel.dirty === false);
    assert.equal(harness.state.savedSchemas.at(-1).variables[0].initialValue, 9007199254740991);
    assert.equal(harness.state.savedSchemas.at(-1).variables[0].min, -9007199254740991);
    assert.equal(harness.state.savedSchemas.at(-1).variables[0].max, 9007199254740991);

    await harness.page.fill('input[data-field="initialValueText"]', '9007199254740992');
    await harness.page.click('[data-action="save"]');
    await harness.page.waitForFunction(() => Boolean(window.__panel.fieldErrors.initialValue));
    const state = await harness.page.evaluate(() => ({
      error: window.__panel.fieldErrors.initialValue,
      savedCount: window.__panel.dirty
    }));
    assert.match(state.error, /瓒呭嚭鍓嶇瀹夊叏鏁存暟鑼冨洿|超出前端安全整数范围/);
    assert.equal(harness.state.savedSchemas.length, 1);
  } finally {
    await harness.close();
  }
});

test('browser interaction closes the manager with Escape when there are no pending edits', async () => {
  const harness = await startBrowserHarness();
  try {
    const project = createInteractiveProject({ single: true });
    await setupGlobalVariablePanel(harness.page, project);
    await harness.page.press('.gv-manager', 'Escape');
    await harness.page.waitForFunction(() => window.__panel.isOpen === false);
    const isOpen = await harness.page.evaluate(() => ({
      panelOpen: window.__panel.isOpen,
      overlays: document.querySelectorAll('.gv-manager-overlay').length
    }));
    assert.equal(isOpen.panelOpen, false);
    assert.equal(isOpen.overlays, 0);
  } finally {
    await harness.close();
  }
});

test('property panel keeps one global-variable helper implementation per method', () => {
  const root = path.resolve(process.cwd(), '../../..');
  const source = fs.readFileSync(path.join(root, 'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanel.js'), 'utf8');
  assert.equal((source.match(/\n\s+applyGlobalVariableInputState\(\)/g) || []).length, 1);
  assert.equal((source.match(/\n\s+renderGlobalVariableBindingControl\(/g) || []).length, 1);
  assert.equal((source.match(/\n\s+isVariableCompatibleWithParameter\(/g) || []).length, 1);
  assert.doesNotMatch(source, /input\.title[\s\S]{0,120}input\.title/);
});
