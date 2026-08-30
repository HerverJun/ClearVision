import test from 'node:test';
import assert from 'node:assert/strict';

function createStorage() {
  const values = new Map();
  return {
    getItem(key) { return values.has(key) ? values.get(key) : null; },
    setItem(key, value) { values.set(key, String(value)); },
    removeItem(key) { values.delete(key); }
  };
}

const windowHandlers = new Map();
const webViewHandlers = new Map();
const postedMessages = [];
const sessionStorage = createStorage();
const localStorage = createStorage();

globalThis.CustomEvent = class CustomEvent {
  constructor(type, init = {}) {
    this.type = type;
    this.detail = init.detail;
  }
};

globalThis.window = {
  sessionStorage,
  localStorage,
  addEventListener(type, handler) {
    if (!windowHandlers.has(type)) windowHandlers.set(type, new Set());
    windowHandlers.get(type).add(handler);
  },
  removeEventListener(type, handler) {
    windowHandlers.get(type)?.delete(handler);
  },
  dispatchEvent(event) {
    for (const handler of windowHandlers.get(event.type) || []) handler(event);
    return true;
  },
  chrome: {
    webview: {
      addEventListener(type, handler) {
        if (!webViewHandlers.has(type)) webViewHandlers.set(type, new Set());
        webViewHandlers.get(type).add(handler);
      },
      removeEventListener(type, handler) {
        webViewHandlers.get(type)?.delete(handler);
      },
      postMessage(message) {
        postedMessages.push(structuredClone(message));
      }
    }
  }
};

const authStorage = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/auth/authStorage.js');
const { default: webMessageBridge } = await import(
  '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/webMessageBridge.js'
);

test('WebMessage bridge attaches the session token centrally and rotates binding on logout', async (t) => {
  const originalDebug = console.debug;
  const debugLines = [];
  console.debug = (...args) => debugLines.push(args.map(value =>
    typeof value === 'string' ? value : JSON.stringify(value)).join(' '));
  globalThis.CV_DEBUG_WEBMESSAGE = true;

  t.after(() => {
    webMessageBridge.mockMode = false;
    webMessageBridge.clearPendingRequests(new Error('test cleanup'));
    authStorage.clearAuthSession();
    globalThis.CV_DEBUG_WEBMESSAGE = false;
    console.debug = originalDebug;
  });

  postedMessages.length = 0;
  authStorage.storeAuthSession('secret-owner-a-token', { userId: 'owner-a' });
  await webMessageBridge.sendMessage('PickFileCommand', {
    payload: { purpose: 'calibration-image' }
  });

  const command = postedMessages.findLast(message => message.messageType === 'PickFileCommand');
  assert.ok(command);
  assert.equal(command.bridge.token, 'secret-owner-a-token');
  assert.match(command.bridge.bindingId, /\S/);
  assert.equal(command.bridge.navigationEpoch, 1);
  assert.equal(Object.hasOwn(command, 'token'), false);
  assert.equal(Object.hasOwn(command.payload, 'token'), false);
  const ownerABinding = command.bridge.bindingId;

  const pending = webMessageBridge.sendMessage('AiSessionListCommand', { payload: {} }, true);
  authStorage.clearAuthSession();
  const pendingFailure = await pending.then(
    () => null,
    error => error
  );

  assert.match(pendingFailure?.message || '', /Authentication binding changed/);
  assert.notEqual(webMessageBridge.bindingId, ownerABinding);
  const logoutBinding = postedMessages.findLast(message => message.messageType === 'BridgeBindingChanged');
  assert.ok(logoutBinding);
  assert.equal(logoutBinding.bridge.token, '');
  assert.equal(logoutBinding.bridge.bindingId, webMessageBridge.bindingId);
  assert.equal(authStorage.getStoredToken(), null);

  authStorage.storeAuthSession('secret-debug-token', { userId: 'owner-b' });
  debugLines.length = 0;
  webMessageBridge.mockMode = true;
  await webMessageBridge.sendMessage('DebugRedactionProbe', { payload: { value: 1 } });
  assert.equal(debugLines.join('\n').includes('secret-debug-token'), false);
  assert.match(debugLines.join('\n'), /DebugRedactionProbe/);
});
