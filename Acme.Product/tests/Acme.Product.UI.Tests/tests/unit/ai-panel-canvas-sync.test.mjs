import test from 'node:test';
import assert from 'node:assert/strict';

function installDom() {
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
      return {
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
