import test from 'node:test';
import assert from 'node:assert/strict';
import { EventBus } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/app/eventBus.js';
import { ServiceRegistry } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/app/serviceRegistry.js';
import { createFlowCanvasAdapter } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvasAdapter.js';
import { bindToolbarCommands } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/app/commandHandlers.js';
import { getFlowNodeCount } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/app/flowData.js';
import httpClient from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/httpClient.js';
import { ProjectView } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/project/projectView.js';
import projectManager, {
  getCurrentProject,
  getProjectList,
  setCurrentProject
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/project/projectManager.js';
import {
  getCategoryIconPath,
  getOperatorColor
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/shared/operatorVisuals.js';

test('event bus publishes and unsubscribes frontend events', () => {
  const bus = new EventBus();
  const received = [];
  const unsubscribe = bus.on('inspection:result', payload => received.push(payload.id));

  bus.emit('inspection:result', { id: 'r1' });
  unsubscribe();
  bus.emit('inspection:result', { id: 'r2' });

  assert.deepEqual(received, ['r1']);
});

test('service registry stores services and notifies subscribers', () => {
  const registry = new ServiceRegistry();
  const observed = [];
  registry.subscribe('flowCanvas', service => observed.push(service?.id ?? null), { immediate: true });

  registry.register('flowCanvas', { id: 'canvas-1' });
  registry.unregister('flowCanvas');

  assert.deepEqual(observed, [null, 'canvas-1', null]);
});

test('flow canvas adapter exposes a stable canvas facade and emits flow changes', () => {
  const emitted = [];
  const fakeCanvas = {
    nodes: new Map(),
    selectedNode: null,
    revision: 0,
    serialize() {
      return { revision: this.revision, nodes: [...this.nodes.keys()] };
    },
    deserialize(flow) {
      this.revision = flow.revision;
    },
    clear() {
      this.nodes.clear();
      this.revision += 1;
    },
    addNode(type, x, y) {
      const node = { id: `${type}-${x}-${y}`, type, x, y };
      this.nodes.set(node.id, node);
      this.revision += 1;
      return node;
    },
    resize() {},
    render() {},
    markFlowStructureChanged() {
      this.revision += 1;
    },
    subscribeStructureState() {
      return () => {};
    },
    getFlowRevision() {
      return this.revision;
    }
  };

  const adapter = createFlowCanvasAdapter(fakeCanvas, {
    eventBus: { emit: (eventName, payload) => emitted.push({ eventName, payload }) }
  });

  const node = adapter.addNode('Thresholding', 10, 20);
  adapter.selectNode(node.id);

  assert.equal(adapter.selectedNode, node.id);
  assert.equal(adapter.nodes.has(node.id), true);
  assert.equal(emitted.at(-1).eventName, 'flow:changed');
  assert.equal(emitted.at(-1).payload.reason, 'addNode');
});

test('flow node count supports canvas serialized operator shapes', () => {
  assert.equal(getFlowNodeCount({
    operators: [{ id: 'op-1' }],
    connections: []
  }), 1);
  assert.equal(getFlowNodeCount({
    Operators: [{ Id: 'op-1' }, { Id: 'op-2' }]
  }), 2);
  assert.equal(getFlowNodeCount({
    nodes: { node1: {}, node2: {} }
  }), 2);
  assert.equal(getFlowNodeCount({
    operators: [],
    nodes: [{ id: 'legacy-node' }]
  }), 1);
  assert.equal(getFlowNodeCount({ connections: [] }), 0);
});

test('toolbar save syncs serialized canvas flow into project manager before persisting', async () => {
  let saveClick = null;
  const saveButton = {
    dataset: {},
    addEventListener(eventName, listener) {
      if (eventName === 'click') {
        saveClick = listener;
      }
    },
    removeEventListener() {}
  };
  const documentRef = {
    getElementById(id) {
      return id === 'btn-save' ? saveButton : null;
    }
  };
  const project = { id: 'project-1', name: 'Project One' };
  const serializedFlow = { operators: [{ id: 'node-1' }], connections: [] };
  const updates = [];
  const saves = [];

  bindToolbarCommands({
    documentRef,
    serviceRegistry: { get() { return null; } },
    getPropertyPanel: () => null,
    getCurrentProject: () => project,
    getFlowCanvas: () => ({
      serialize() {
        return serializedFlow;
      }
    }),
    getImageViewer: () => null,
    projectManager: {
      updateFlow(flow) {
        updates.push(flow);
        project.flow = flow;
      },
      getCurrentProject() {
        return project;
      },
      async saveProject(projectToSave) {
        saves.push(projectToSave);
      }
    },
    inspectionController: {},
    showToast() {},
    handleNewProject() {},
    setCurrentView() {},
    syncActiveNavButton() {},
    async switchView() {},
    async ensureInspectionPanelReady() {},
    initializeInspectionImageViewer() {},
    async logout() {}
  });

  saveClick({ preventDefault() {} });
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.equal(updates.length, 1);
  assert.equal(updates[0], serializedFlow);
  assert.equal(saves.length, 1);
  assert.equal(saves[0].flow, serializedFlow);
});

test('toolbar save persists draft flows without requiring run-ready validation', async () => {
  let saveClick = null;
  const saveButton = {
    dataset: {},
    addEventListener(eventName, listener) {
      if (eventName === 'click') {
        saveClick = listener;
      }
    },
    removeEventListener() {}
  };
  const documentRef = {
    getElementById(id) {
      return id === 'btn-save' ? saveButton : null;
    }
  };
  const project = { id: 'project-draft', name: 'Draft Project' };
  const serializedFlow = { operators: [{ id: 'node-1' }], connections: [] };
  let validationCalls = 0;
  let draftSyncs = 0;
  const saves = [];

  bindToolbarCommands({
    documentRef,
    serviceRegistry: { get() { return null; } },
    getPropertyPanel: () => ({
      currentOperator: { id: 'node-1' },
      validateFlowForAction() {
        validationCalls += 1;
        return false;
      },
      syncDraftChanges() {
        draftSyncs += 1;
        return true;
      }
    }),
    getCurrentProject: () => project,
    getFlowCanvas: () => ({
      serialize() {
        return serializedFlow;
      }
    }),
    getImageViewer: () => null,
    projectManager: {
      updateFlow(flow) {
        project.flow = flow;
      },
      getCurrentProject() {
        return project;
      },
      async saveProject(projectToSave) {
        saves.push(projectToSave);
      }
    },
    inspectionController: {},
    showToast() {},
    handleNewProject() {},
    setCurrentView() {},
    syncActiveNavButton() {},
    async switchView() {},
    async ensureInspectionPanelReady() {},
    initializeInspectionImageViewer() {},
    async logout() {}
  });

  saveClick({ preventDefault() {} });
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.equal(validationCalls, 0);
  assert.equal(draftSyncs, 1);
  assert.equal(saves.length, 1);
  assert.equal(saves[0].flow, serializedFlow);
});

test('toolbar run still requires run-ready flow validation', async () => {
  let runClick = null;
  const runButton = {
    dataset: {},
    addEventListener(eventName, listener) {
      if (eventName === 'click') {
        runClick = listener;
      }
    },
    removeEventListener() {}
  };
  const documentRef = {
    getElementById(id) {
      return id === 'btn-run' ? runButton : null;
    }
  };
  let validationCalls = 0;
  let switched = false;
  let executed = false;

  bindToolbarCommands({
    documentRef,
    serviceRegistry: { get() { return null; } },
    getPropertyPanel: () => ({
      validateFlowForAction() {
        validationCalls += 1;
        return false;
      }
    }),
    getCurrentProject: () => ({ id: 'project-1', name: 'Project One' }),
    getFlowCanvas: () => ({
      nodes: new Map([['node-1', { id: 'node-1' }]])
    }),
    getImageViewer: () => null,
    projectManager: {},
    inspectionController: {
      setProject() {},
      async executeSingle() {
        executed = true;
      }
    },
    showToast() {},
    handleNewProject() {},
    setCurrentView() {},
    syncActiveNavButton() {},
    async switchView() {
      switched = true;
    },
    async ensureInspectionPanelReady() {},
    initializeInspectionImageViewer() {},
    async logout() {}
  });

  runClick({ preventDefault() {} });
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.equal(validationCalls, 1);
  assert.equal(switched, false);
  assert.equal(executed, false);
});

test('operator visual registry supports localized category groups', () => {
  const defaultIcon = getCategoryIconPath('__missing-category__');
  const localizedCategories = [
    'AI Detection',
    'AI 检测',
    'Communication',
    'Detection',
    'Flow Control',
    '频域',
    '区域处理',
    '纹理'
  ];

  for (const category of localizedCategories) {
    assert.notEqual(getCategoryIconPath(category), defaultIcon);
    assert.notEqual(getOperatorColor('__missing-operator__', category), '#595959');
  }
});

test('project view falls back to createdAt when modifiedAt is missing', () => {
  const view = Object.create(ProjectView.prototype);
  const createdAt = '2026-06-02T12:00:00.000Z';
  assert.equal(
    view.formatProjectDate(null, createdAt),
    new Date(createdAt).toLocaleDateString('zh-CN')
  );

  view.sortBy = 'modifiedAt';
  view.sortOrder = 'desc';
  view.filteredProjects = [
    { id: 'older-created', createdAt: '2026-05-01T12:00:00.000Z', modifiedAt: null },
    { id: 'newer-created', createdAt: '2026-06-01T12:00:00.000Z', modifiedAt: null }
  ];

  view.sortProjects();

  assert.deepEqual(view.filteredProjects.map(project => project.id), [
    'newer-created',
    'older-created'
  ]);
});

test('project manager save updates current project and list cache for explicit flow saves', async (t) => {
  const originalPut = httpClient.put;
  const previousDocument = globalThis.document;
  const project = {
    id: 'project-save-sync',
    name: 'Before',
    description: 'Old description',
    version: '1.0.0',
    createdAt: '2026-06-01T12:00:00.000Z',
    modifiedAt: null
  };
  const flow = { operators: [{ id: 'node-1' }], connections: [] };
  const puts = [];

  t.after(() => {
    httpClient.put = originalPut;
    projectManager.currentProject = null;
    setCurrentProject(null);
    if (previousDocument === undefined) {
      delete globalThis.document;
    } else {
      globalThis.document = previousDocument;
    }
  });

  globalThis.document = {
    title: '',
    getElementById() {
      return null;
    }
  };
  projectManager.currentProject = project;
  setCurrentProject(project);
  httpClient.put = async (url, body) => {
    puts.push({ url, body });
    return { ok: true };
  };

  await projectManager.saveProject({
    ...project,
    name: 'After',
    description: 'New description',
    flow
  });

  assert.deepEqual(puts.map(call => call.url), [
    '/projects/project-save-sync',
    '/projects/project-save-sync/flow'
  ]);
  assert.equal(getCurrentProject().name, 'After');
  assert.equal(getCurrentProject().description, 'New description');
  assert.equal(getCurrentProject().flow, flow);
  assert.equal(getProjectList()[0].id, 'project-save-sync');
  assert.equal(getProjectList()[0].flow, flow);
});

test('project manager save flushes registered flow snapshot provider before persisting', async (t) => {
  const originalPut = httpClient.put;
  const previousDocument = globalThis.document;
  const staleFlow = { operators: [{ id: 'stale-node' }], connections: [] };
  const freshFlow = { operators: [{ id: 'fresh-node' }], connections: [] };
  const project = {
    id: 'project-provider-flow',
    name: 'Provider Flow',
    description: '',
    flow: staleFlow
  };
  const puts = [];
  let providerCalls = 0;

  t.after(() => {
    httpClient.put = originalPut;
    projectManager.setFlowSnapshotProvider(null);
    projectManager.currentProject = null;
    projectManager.unsavedChanges = false;
    projectManager.forgetProjectFromCaches(project.id);
    setCurrentProject(null);
    if (previousDocument === undefined) {
      delete globalThis.document;
    } else {
      globalThis.document = previousDocument;
    }
  });

  globalThis.document = {
    title: '',
    getElementById() {
      return null;
    }
  };
  projectManager.currentProject = project;
  projectManager.unsavedChanges = true;
  setCurrentProject(project);
  projectManager.setFlowSnapshotProvider((currentProject) => {
    providerCalls += 1;
    assert.equal(currentProject.id, project.id);
    return freshFlow;
  });
  httpClient.put = async (url, body) => {
    puts.push({ url, body });
    return { ok: true };
  };

  await projectManager.saveProject();

  assert.equal(providerCalls, 1);
  assert.deepEqual(puts.map(call => call.url), [
    '/projects/project-provider-flow',
    '/projects/project-provider-flow/flow'
  ]);
  assert.deepEqual(puts[1].body, freshFlow);
  assert.equal(getCurrentProject().flow, freshFlow);
  assert.equal(projectManager.unsavedChanges, false);
});

test('project manager flow save omits unchanged global variables from aggregate payload', async (t) => {
  const originalPut = httpClient.put;
  const previousDocument = globalThis.document;
  const schema = {
    schemaVersion: '1.0',
    variables: [{ id: 'var-count', name: 'judge.count', valueType: 'Int64', initialValue: '1' }],
    sourceBindings: [],
    targetBindings: []
  };
  const project = {
    id: 'project-flow-only-save',
    name: 'Flow Only',
    description: '',
    flow: { operators: [], connections: [] },
    globalVariables: schema
  };
  const flow = { operators: [{ id: 'node-1' }], connections: [] };
  const puts = [];

  t.after(() => {
    httpClient.put = originalPut;
    projectManager.currentProject = null;
    projectManager.savedGlobalVariablesSignature = '';
    projectManager.forgetProjectFromCaches(project.id);
    setCurrentProject(null);
    if (previousDocument === undefined) {
      delete globalThis.document;
    } else {
      globalThis.document = previousDocument;
    }
  });

  globalThis.document = {
    title: '',
    getElementById() {
      return null;
    }
  };
  projectManager.currentProject = project;
  projectManager.rememberGlobalVariableBaseline(project);
  setCurrentProject(project);
  httpClient.put = async (url, body) => {
    puts.push({ url, body });
    return { ok: true };
  };

  await projectManager.saveProject({ ...project, flow });

  assert.deepEqual(puts.map(call => call.url), [
    '/projects/project-flow-only-save',
    '/projects/project-flow-only-save/flow'
  ]);
  assert.equal(Object.hasOwn(puts[0].body, 'globalVariables'), false);
  assert.deepEqual(puts[1].body, flow);
});

test('project manager includes changed global variables in aggregate save payload', async (t) => {
  const originalPut = httpClient.put;
  const previousDocument = globalThis.document;
  const schema = {
    schemaVersion: '1.0',
    variables: [{ id: 'var-count', name: 'judge.count', valueType: 'Int64', initialValue: '1' }],
    sourceBindings: [],
    targetBindings: []
  };
  const changedSchema = {
    ...schema,
    targetBindings: [{ id: 'bind-target', variableId: 'var-count', operatorId: 'op-1', parameterId: 'p-1' }]
  };
  const project = {
    id: 'project-schema-save',
    name: 'Schema Save',
    description: '',
    flow: { operators: [], connections: [] },
    globalVariables: schema
  };
  const puts = [];

  t.after(() => {
    httpClient.put = originalPut;
    projectManager.currentProject = null;
    projectManager.savedGlobalVariablesSignature = '';
    setCurrentProject(null);
    if (previousDocument === undefined) {
      delete globalThis.document;
    } else {
      globalThis.document = previousDocument;
    }
  });

  globalThis.document = {
    title: '',
    getElementById() {
      return null;
    }
  };
  projectManager.currentProject = project;
  projectManager.rememberGlobalVariableBaseline(project);
  setCurrentProject(project);
  httpClient.put = async (url, body) => {
    puts.push({ url, body });
    return { ...project, globalVariables: changedSchema };
  };

  await projectManager.saveProject({ ...project, globalVariables: changedSchema });

  assert.equal(puts[0].url, '/projects/project-schema-save');
  assert.equal(puts[0].body.globalVariables, changedSchema);
  assert.equal(getCurrentProject().globalVariables, changedSchema);
});

test('project manager global variable save uses schema endpoint without flow payload', async (t) => {
  const originalPut = httpClient.put;
  const previousDocument = globalThis.document;
  const project = {
    id: 'project-schema-only',
    name: 'Schema Only',
    description: '',
    flow: { operators: [{ id: 'node-1' }], connections: [] },
    globalVariables: {
      schemaVersion: '1.0',
      variables: [{ id: 'var-count', name: 'judge.count', valueType: 'Int64', initialValue: '1' }],
      sourceBindings: [],
      targetBindings: []
    }
  };
  const changedSchema = {
    ...project.globalVariables,
    targetBindings: [{ id: 'bind-target', variableId: 'var-count', operatorId: 'op-1', parameterId: 'p-1' }]
  };
  const puts = [];

  t.after(() => {
    httpClient.put = originalPut;
    projectManager.currentProject = null;
    projectManager.savedGlobalVariablesSignature = '';
    setCurrentProject(null);
    if (previousDocument === undefined) {
      delete globalThis.document;
    } else {
      globalThis.document = previousDocument;
    }
  });

  globalThis.document = {
    title: '',
    getElementById() {
      return null;
    }
  };
  projectManager.currentProject = project;
  projectManager.rememberGlobalVariableBaseline(project);
  setCurrentProject(project);
  httpClient.put = async (url, body) => {
    puts.push({ url, body });
    return body;
  };

  const saved = await projectManager.saveGlobalVariables(changedSchema);

  assert.equal(puts.length, 1);
  assert.equal(puts[0].url, '/projects/project-schema-only/global-variables');
  assert.equal(Object.hasOwn(puts[0].body, 'flow'), false);
  assert.equal(puts[0].body.variables[0].id, 'var-count');
  assert.equal(puts[0].body.targetBindings[0].id, 'bind-target');
  assert.equal(saved.targetBindings[0].id, 'bind-target');
  assert.equal(getCurrentProject().flow, project.flow);
  assert.equal(getCurrentProject().globalVariables.targetBindings[0].id, 'bind-target');
  assert.equal(getProjectList().find(item => item.id === project.id).globalVariables.targetBindings[0].id, 'bind-target');
  assert.equal(projectManager.savedGlobalVariablesSignature, JSON.stringify(saved));
});

test('project manager drops delayed global variable save application after project switch', async (t) => {
  const originalPut = httpClient.put;
  const previousDocument = globalThis.document;
  const projectA = {
    id: 'project-save-a',
    name: 'Project A',
    flow: { operators: [{ id: 'node-a' }], connections: [] },
    globalVariables: {
      schemaVersion: '1.0',
      variables: [{ id: 'var-a', name: 'a.value', valueType: 'String', initialValue: 'a' }],
      sourceBindings: [],
      targetBindings: []
    }
  };
  const projectB = {
    id: 'project-save-b',
    name: 'Project B',
    flow: { operators: [{ id: 'node-b' }], connections: [] },
    globalVariables: {
      schemaVersion: '1.0',
      variables: [{ id: 'var-b', name: 'b.value', valueType: 'String', initialValue: 'b' }],
      sourceBindings: [],
      targetBindings: []
    }
  };
  const changedSchemaA = {
    ...projectA.globalVariables,
    sourceBindings: [{ id: 'source-a', variableId: 'var-a', operatorId: 'node-a', outputPortId: 'out-a' }]
  };
  const savedSchemaA = {
    ...changedSchemaA,
    variables: [{ ...changedSchemaA.variables[0], displayName: 'Saved A' }]
  };
  const puts = [];
  let resolvePut;

  t.after(() => {
    httpClient.put = originalPut;
    projectManager.currentProject = null;
    projectManager.unsavedChanges = false;
    projectManager.savedGlobalVariablesSignature = '';
    projectManager.forgetProjectFromCaches(projectA.id);
    projectManager.forgetProjectFromCaches(projectB.id);
    setCurrentProject(null);
    if (previousDocument === undefined) {
      delete globalThis.document;
    } else {
      globalThis.document = previousDocument;
    }
  });

  globalThis.document = {
    title: '',
    getElementById() {
      return null;
    }
  };
  projectManager.currentProject = projectA;
  projectManager.unsavedChanges = false;
  projectManager.rememberGlobalVariableBaseline(projectA);
  setCurrentProject(projectA);
  httpClient.put = async (url, body) => {
    puts.push({ url, body });
    return new Promise(resolve => {
      resolvePut = resolve;
    });
  };

  const savePromise = projectManager.saveGlobalVariables(changedSchemaA);
  await Promise.resolve();
  assert.equal(puts.length, 1);
  assert.equal(puts[0].url, '/projects/project-save-a/global-variables');
  assert.equal(puts[0].body.sourceBindings[0].id, 'source-a');

  projectManager.currentProject = projectB;
  projectManager.unsavedChanges = true;
  projectManager.rememberGlobalVariableBaseline(projectB);
  projectManager.rememberProjectInCaches(projectB);
  setCurrentProject(projectB);
  resolvePut(savedSchemaA);

  const saved = await savePromise;
  const cachedProjectB = getProjectList().find(project => project.id === projectB.id);

  assert.equal(saved.variables[0].displayName, 'Saved A');
  assert.equal(projectManager.currentProject.id, projectB.id);
  assert.equal(projectManager.currentProject.name, 'Project B');
  assert.equal(projectManager.currentProject.flow.operators[0].id, 'node-b');
  assert.equal(projectManager.currentProject.globalVariables.variables[0].id, 'var-b');
  assert.equal(projectManager.currentProject.globalVariables.sourceBindings.length, 0);
  assert.equal(projectManager.unsavedChanges, true);
  assert.equal(getCurrentProject().id, projectB.id);
  assert.equal(getCurrentProject().globalVariables.variables[0].id, 'var-b');
  assert.equal(cachedProjectB.globalVariables.variables[0].id, 'var-b');
  assert.equal(cachedProjectB.globalVariables.sourceBindings.length, 0);
  assert.equal(projectManager.savedGlobalVariablesSignature, JSON.stringify(projectB.globalVariables));
});

test('project manager close waits for confirmed save before clearing current project', async (t) => {
  const originalPut = httpClient.put;
  const previousDocument = globalThis.document;
  const previousWindow = globalThis.window;
  const project = {
    id: 'project-close-save',
    name: 'Needs Save',
    description: '',
    version: '1.0.0',
    flow: { operators: [{ id: 'node-1' }], connections: [] }
  };
  const puts = [];
  let resolveFirstPut;

  t.after(() => {
    httpClient.put = originalPut;
    projectManager.currentProject = null;
    projectManager.unsavedChanges = false;
    setCurrentProject(null);
    if (previousDocument === undefined) {
      delete globalThis.document;
    } else {
      globalThis.document = previousDocument;
    }
    if (previousWindow === undefined) {
      delete globalThis.window;
    } else {
      globalThis.window = previousWindow;
    }
  });

  globalThis.document = {
    title: '',
    getElementById() {
      return null;
    }
  };
  globalThis.window = {
    confirm() {
      return true;
    }
  };
  projectManager.currentProject = project;
  projectManager.unsavedChanges = true;
  setCurrentProject(project);
  httpClient.put = async (url, body) => {
    puts.push({ url, body });
    if (puts.length === 1) {
      await new Promise(resolve => {
        resolveFirstPut = resolve;
      });
    }
    return { ok: true };
  };

  const closePromise = projectManager.closeProject();
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.equal(projectManager.currentProject?.id, 'project-close-save');
  resolveFirstPut();
  await closePromise;

  assert.deepEqual(puts.map(call => call.url), [
    '/projects/project-close-save',
    '/projects/project-close-save/flow'
  ]);
  assert.equal(projectManager.currentProject, null);
  assert.equal(getCurrentProject(), null);
});

test('project manager saves dirty project before opening another project', async (t) => {
  const originalGet = httpClient.get;
  const originalPut = httpClient.put;
  const previousDocument = globalThis.document;
  const previousWindow = globalThis.window;
  const calls = [];
  const current = {
    id: 'project-current-dirty',
    name: 'Current',
    description: '',
    version: '1.0.0',
    flow: { operators: [{ id: 'dirty-node' }], connections: [] }
  };
  const next = {
    id: 'project-next',
    name: 'Next',
    description: '',
    version: '1.0.0',
    flow: { operators: [], connections: [] }
  };

  t.after(() => {
    httpClient.get = originalGet;
    httpClient.put = originalPut;
    projectManager.currentProject = null;
    projectManager.unsavedChanges = false;
    setCurrentProject(null);
    if (previousDocument === undefined) {
      delete globalThis.document;
    } else {
      globalThis.document = previousDocument;
    }
    if (previousWindow === undefined) {
      delete globalThis.window;
    } else {
      globalThis.window = previousWindow;
    }
  });

  globalThis.document = {
    title: '',
    getElementById() {
      return null;
    }
  };
  globalThis.window = {
    confirm() {
      calls.push('confirm-save');
      return true;
    }
  };
  projectManager.currentProject = current;
  projectManager.unsavedChanges = true;
  setCurrentProject(current);
  httpClient.put = async (url) => {
    calls.push(`put:${url}`);
    return { ok: true };
  };
  httpClient.get = async (url) => {
    calls.push(`get:${url}`);
    return next;
  };

  const opened = await projectManager.openProject('project-next');

  assert.equal(opened, next);
  assert.deepEqual(calls, [
    'confirm-save',
    'put:/projects/project-current-dirty',
    'put:/projects/project-current-dirty/flow',
    'get:/projects/project-next'
  ]);
  assert.equal(projectManager.currentProject.id, 'project-next');
  assert.equal(projectManager.unsavedChanges, false);
});

test('project manager ignores stale openProject responses', async (t) => {
  const originalGet = httpClient.get;
  const previousDocument = globalThis.document;
  const previousWindow = globalThis.window;
  const resolvers = new Map();

  t.after(() => {
    httpClient.get = originalGet;
    projectManager.currentProject = null;
    projectManager.unsavedChanges = false;
    projectManager.openProjectRequestId = 0;
    setCurrentProject(null);
    if (previousDocument === undefined) {
      delete globalThis.document;
    } else {
      globalThis.document = previousDocument;
    }
    if (previousWindow === undefined) {
      delete globalThis.window;
    } else {
      globalThis.window = previousWindow;
    }
  });

  globalThis.document = {
    title: '',
    getElementById() {
      return null;
    }
  };
  globalThis.window = {
    confirm() {
      return true;
    }
  };
  projectManager.currentProject = null;
  projectManager.unsavedChanges = false;
  setCurrentProject(null);
  httpClient.get = async (url) => new Promise(resolve => {
    resolvers.set(url, resolve);
  });

  const firstOpen = projectManager.openProject('project-a');
  const secondOpen = projectManager.openProject('project-b');
  await Promise.resolve();

  resolvers.get('/projects/project-b')({
    id: 'project-b',
    name: 'Project B',
    flow: { operators: [], connections: [] }
  });
  await secondOpen;
  resolvers.get('/projects/project-a')({
    id: 'project-a',
    name: 'Project A',
    flow: { operators: [], connections: [] }
  });
  const staleResult = await firstOpen;

  assert.equal(staleResult, null);
  assert.equal(projectManager.currentProject.id, 'project-b');
  assert.equal(getCurrentProject().id, 'project-b');
});

test('project manager ignores stale openProject response after creating a new project', async (t) => {
  const originalGet = httpClient.get;
  const originalPost = httpClient.post;
  const previousDocument = globalThis.document;
  const previousWindow = globalThis.window;
  let resolveOpen;

  t.after(() => {
    httpClient.get = originalGet;
    httpClient.post = originalPost;
    projectManager.currentProject = null;
    projectManager.unsavedChanges = false;
    projectManager.openProjectRequestId = 0;
    setCurrentProject(null);
    if (previousDocument === undefined) {
      delete globalThis.document;
    } else {
      globalThis.document = previousDocument;
    }
    if (previousWindow === undefined) {
      delete globalThis.window;
    } else {
      globalThis.window = previousWindow;
    }
  });

  globalThis.document = {
    title: '',
    getElementById() {
      return null;
    }
  };
  globalThis.window = {
    confirm() {
      return true;
    }
  };
  projectManager.currentProject = null;
  projectManager.unsavedChanges = false;
  projectManager.openProjectRequestId = 0;
  setCurrentProject(null);
  httpClient.get = async (url) => new Promise(resolve => {
    assert.equal(url, '/projects/project-a');
    resolveOpen = resolve;
  });
  httpClient.post = async (url, body) => {
    assert.equal(url, '/projects');
    return {
      id: 'project-created-after-open',
      name: body.name,
      description: body.description
    };
  };

  const openPromise = projectManager.openProject('project-a');
  await Promise.resolve();

  const created = await projectManager.createProject('Created After Open', '');
  assert.equal(projectManager.currentProject.id, 'project-created-after-open');

  resolveOpen({
    id: 'project-a',
    name: 'Project A',
    flow: { operators: [], connections: [] }
  });
  const staleResult = await openPromise;

  assert.equal(staleResult, null);
  assert.equal(created.id, 'project-created-after-open');
  assert.equal(projectManager.currentProject.id, 'project-created-after-open');
  assert.equal(getCurrentProject().id, 'project-created-after-open');
});

test('project manager does not reload the same dirty project from the server', async (t) => {
  const originalGet = httpClient.get;
  const originalPut = httpClient.put;
  const previousDocument = globalThis.document;
  const previousWindow = globalThis.window;
  const calls = [];
  const current = {
    id: 'project-same-dirty',
    name: 'Same Project',
    description: '',
    flow: { operators: [{ id: 'dirty-node' }], connections: [] }
  };

  t.after(() => {
    httpClient.get = originalGet;
    httpClient.put = originalPut;
    projectManager.currentProject = null;
    projectManager.unsavedChanges = false;
    setCurrentProject(null);
    if (previousDocument === undefined) {
      delete globalThis.document;
    } else {
      globalThis.document = previousDocument;
    }
    if (previousWindow === undefined) {
      delete globalThis.window;
    } else {
      globalThis.window = previousWindow;
    }
  });

  globalThis.document = {
    title: '',
    getElementById() {
      return null;
    }
  };
  globalThis.window = {
    confirm() {
      calls.push('confirm-save');
      return true;
    }
  };
  projectManager.currentProject = current;
  projectManager.unsavedChanges = true;
  setCurrentProject(current);
  httpClient.put = async (url) => {
    calls.push(`put:${url}`);
    return { ok: true };
  };
  httpClient.get = async (url) => {
    calls.push(`get:${url}`);
    return { id: 'project-same-dirty', name: 'Server Copy' };
  };

  const opened = await projectManager.openProject('project-same-dirty');

  assert.equal(opened, current);
  assert.deepEqual(calls, [
    'confirm-save',
    'put:/projects/project-same-dirty',
    'put:/projects/project-same-dirty/flow'
  ]);
  assert.equal(projectManager.currentProject.name, 'Same Project');
  assert.equal(projectManager.unsavedChanges, false);
});

test('project manager saves dirty project before creating a new project', async (t) => {
  const originalPost = httpClient.post;
  const originalPut = httpClient.put;
  const previousDocument = globalThis.document;
  const previousWindow = globalThis.window;
  const calls = [];
  const current = {
    id: 'project-current-before-create',
    name: 'Current',
    description: '',
    version: '1.0.0',
    flow: { operators: [{ id: 'dirty-node' }], connections: [] }
  };
  const created = {
    id: 'project-created',
    name: 'Created',
    description: '',
    version: '1.0.0'
  };

  t.after(() => {
    httpClient.post = originalPost;
    httpClient.put = originalPut;
    projectManager.currentProject = null;
    projectManager.unsavedChanges = false;
    setCurrentProject(null);
    if (previousDocument === undefined) {
      delete globalThis.document;
    } else {
      globalThis.document = previousDocument;
    }
    if (previousWindow === undefined) {
      delete globalThis.window;
    } else {
      globalThis.window = previousWindow;
    }
  });

  globalThis.document = {
    title: '',
    getElementById() {
      return null;
    }
  };
  globalThis.window = {
    confirm() {
      calls.push('confirm-save');
      return true;
    }
  };
  projectManager.currentProject = current;
  projectManager.unsavedChanges = true;
  setCurrentProject(current);
  httpClient.put = async (url) => {
    calls.push(`put:${url}`);
    return { ok: true };
  };
  httpClient.post = async (url, body) => {
    calls.push(`post:${url}:${body.name}`);
    return { ...created, name: body.name, description: body.description };
  };

  const project = await projectManager.createProject('Created', 'New description');

  assert.equal(project.id, 'project-created');
  assert.deepEqual(calls, [
    'confirm-save',
    'put:/projects/project-current-before-create',
    'put:/projects/project-current-before-create/flow',
    'post:/projects:Created'
  ]);
  assert.equal(projectManager.currentProject.id, 'project-created');
  assert.equal(projectManager.unsavedChanges, false);
});

test('project manager delete removes projects from cached lists', async (t) => {
  const originalDelete = httpClient.delete;
  const originalGet = httpClient.get;

  t.after(() => {
    httpClient.delete = originalDelete;
    httpClient.get = originalGet;
  });

  httpClient.get = async (url) => {
    if (url === '/projects' || url.startsWith('/projects/recent')) {
      return [
        { id: 'project-keep', name: 'Keep' },
        { id: 'project-delete', name: 'Delete' }
      ];
    }

    throw new Error(`Unexpected URL: ${url}`);
  };
  httpClient.delete = async (url) => {
    assert.equal(url, '/projects/project-delete');
    return { ok: true };
  };

  await projectManager.getProjectList();
  await projectManager.getRecentProjects();
  await projectManager.deleteProject('project-delete');

  assert.deepEqual(getProjectList().map(project => project.id), ['project-keep']);
});
