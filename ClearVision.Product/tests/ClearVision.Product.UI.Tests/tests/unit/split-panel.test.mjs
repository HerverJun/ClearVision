import test from 'node:test';
import assert from 'node:assert/strict';

function createClassList() {
  const values = new Set();

  return {
    add(value) {
      values.add(value);
    },
    remove(value) {
      values.delete(value);
    },
    contains(value) {
      return values.has(value);
    }
  };
}

function createElement(tagName = 'div') {
  const listeners = new Map();

  return {
    tagName: tagName.toUpperCase(),
    className: '',
    classList: createClassList(),
    style: {},
    parentNode: null,
    children: [],
    _innerHTML: '',
    get innerHTML() {
      return this._innerHTML;
    },
    set innerHTML(value) {
      this._innerHTML = value;
      if (value === '') {
        this.children = [];
      }
    },
    appendChild(child) {
      child.parentNode = this;
      this.children.push(child);
    },
    addEventListener(eventName, listener) {
      listeners.set(eventName, listener);
    },
    dispatchEvent(eventName, event = {}) {
      const listener = listeners.get(eventName);
      if (listener) {
        listener(event);
      }
    }
  };
}

function installSplitPanelDom() {
  const previousWindow = globalThis.window;
  const previousDocument = globalThis.document;
  const documentListeners = new Map();
  const rafCallbacks = new Map();
  const cancelledFrames = [];
  let nextFrameId = 1;

  globalThis.document = {
    body: {
      style: {}
    },
    createElement,
    getElementById() {
      return null;
    },
    addEventListener(eventName, listener) {
      documentListeners.set(eventName, listener);
    }
  };

  globalThis.window = {
    addEventListener() {},
    requestAnimationFrame(callback) {
      const frameId = nextFrameId;
      nextFrameId += 1;
      rafCallbacks.set(frameId, callback);
      return frameId;
    },
    cancelAnimationFrame(frameId) {
      cancelledFrames.push(frameId);
      rafCallbacks.delete(frameId);
    },
    setTimeout(callback) {
      return setTimeout(callback, 16);
    },
    clearTimeout(timeoutId) {
      clearTimeout(timeoutId);
    }
  };

  return {
    documentListeners,
    rafCallbacks,
    cancelledFrames,
    restore() {
      if (previousWindow === undefined) {
        delete globalThis.window;
      } else {
        globalThis.window = previousWindow;
      }

      if (previousDocument === undefined) {
        delete globalThis.document;
      } else {
        globalThis.document = previousDocument;
      }
    }
  };
}

function runNextFrame(rafCallbacks) {
  const nextFrame = rafCallbacks.entries().next().value;
  assert.ok(nextFrame, 'expected a scheduled animation frame');

  const [frameId, callback] = nextFrame;
  rafCallbacks.delete(frameId);
  callback();
}

test('SplitPanel coalesces drag resize updates into one animation frame', async () => {
  const dom = installSplitPanelDom();

  try {
    const { SplitPanel } = await import(
      '../../../../src/ClearVision.Product.Desktop/wwwroot/src/shared/components/splitPanel.js'
    );

    const container = createElement('div');
    let widthReads = 0;
    Object.defineProperty(container, 'clientWidth', {
      get() {
        widthReads += 1;
        return 1000;
      }
    });

    const resizeRatios = [];
    const panel = new SplitPanel(container, {
      initialRatio: 0.4,
      minSize: 100,
      onResize: (ratio) => resizeRatios.push(ratio)
    });

    let prevented = false;
    panel.splitter.dispatchEvent('mousedown', {
      clientX: 400,
      preventDefault() {
        prevented = true;
      }
    });

    dom.documentListeners.get('mousemove')({ clientX: 500 });
    dom.documentListeners.get('mousemove')({ clientX: 650 });
    dom.documentListeners.get('mousemove')({ clientX: 800 });

    assert.equal(prevented, true);
    assert.equal(widthReads, 1);
    assert.equal(dom.rafCallbacks.size, 1);
    assert.deepEqual(resizeRatios, []);
    assert.equal(panel.firstPanel.style.flex, '0 0 40%');

    runNextFrame(dom.rafCallbacks);

    assert.deepEqual(resizeRatios, [0.8]);
    assert.equal(panel.firstPanel.style.flex, '0 0 80%');
    assert.equal(panel.getRatio(), 0.8);

    panel.destroy();
  } finally {
    dom.restore();
  }
});

test('SplitPanel clears stale pending drag frames before synchronous ratio changes', async () => {
  const dom = installSplitPanelDom();

  try {
    const { SplitPanel } = await import(
      '../../../../src/ClearVision.Product.Desktop/wwwroot/src/shared/components/splitPanel.js'
    );

    const container = createElement('div');
    Object.defineProperty(container, 'clientWidth', {
      get() {
        return 1000;
      }
    });

    const resizeRatios = [];
    const panel = new SplitPanel(container, {
      initialRatio: 0.5,
      minSize: 100,
      onResize: (ratio) => resizeRatios.push(ratio)
    });

    panel.splitter.dispatchEvent('mousedown', {
      clientX: 500,
      preventDefault() {}
    });
    dom.documentListeners.get('mousemove')({ clientX: 700 });

    const scheduledFrame = dom.rafCallbacks.values().next().value;
    assert.equal(typeof scheduledFrame, 'function');

    panel.setRatio(0.25);
    scheduledFrame();

    assert.deepEqual(dom.cancelledFrames, [1]);
    assert.deepEqual(resizeRatios, []);
    assert.equal(panel.getRatio(), 0.25);
    assert.equal(panel.firstPanel.style.flex, '0 0 25%');

    panel.collapseSecond();
    assert.equal(panel.getRatio(), 0.95);
    assert.equal(panel.firstPanel.style.flex, '0 0 95%');

    panel.destroy();
  } finally {
    dom.restore();
  }
});
