import test from 'node:test';
import assert from 'node:assert/strict';
import { EventBus } from '../../../../src/Acme.Product.Desktop/wwwroot/src/core/app/eventBus.js';
import { ServiceRegistry } from '../../../../src/Acme.Product.Desktop/wwwroot/src/core/app/serviceRegistry.js';
import { createFlowCanvasAdapter } from '../../../../src/Acme.Product.Desktop/wwwroot/src/core/canvas/flowCanvasAdapter.js';

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
