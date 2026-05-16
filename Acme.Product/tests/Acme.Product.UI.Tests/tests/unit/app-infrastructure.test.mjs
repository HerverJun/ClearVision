import test from 'node:test';
import assert from 'node:assert/strict';
import { EventBus } from '../../../../src/Acme.Product.Desktop/wwwroot/src/core/app/eventBus.js';
import { ServiceRegistry } from '../../../../src/Acme.Product.Desktop/wwwroot/src/core/app/serviceRegistry.js';
import { createFlowCanvasAdapter } from '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/flowCanvasAdapter.js';
import { bindToolbarCommands } from '../../../../src/Acme.Product.Desktop/wwwroot/src/core/app/commandHandlers.js';

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
