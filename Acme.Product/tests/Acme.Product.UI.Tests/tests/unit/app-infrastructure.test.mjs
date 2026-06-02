import test from 'node:test';
import assert from 'node:assert/strict';
import { EventBus } from '../../../../src/Acme.Product.Desktop/wwwroot/src/core/app/eventBus.js';
import { ServiceRegistry } from '../../../../src/Acme.Product.Desktop/wwwroot/src/core/app/serviceRegistry.js';
import { createFlowCanvasAdapter } from '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/flowCanvasAdapter.js';
import { bindToolbarCommands } from '../../../../src/Acme.Product.Desktop/wwwroot/src/core/app/commandHandlers.js';
import { getFlowNodeCount } from '../../../../src/Acme.Product.Desktop/wwwroot/src/core/app/flowData.js';
import httpClient from '../../../../src/Acme.Product.Desktop/wwwroot/src/core/messaging/httpClient.js';
import { ProjectView } from '../../../../src/Acme.Product.Desktop/wwwroot/src/features/project/projectView.js';
import projectManager, {
  getCurrentProject,
  getProjectList,
  setCurrentProject
} from '../../../../src/Acme.Product.Desktop/wwwroot/src/features/project/projectManager.js';
import {
  getCategoryIconPath,
  getOperatorColor
} from '../../../../src/Acme.Product.Desktop/wwwroot/src/shared/operatorVisuals.js';

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
  const localizedCategories = ['频域', '区域处理', '纹理'];

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
