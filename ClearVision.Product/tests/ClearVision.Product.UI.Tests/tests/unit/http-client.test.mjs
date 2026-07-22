import test from 'node:test';
import assert from 'node:assert/strict';
import { HttpClient, HttpError } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/httpClient.js';

function createStorage(initial = {}) {
  const values = new Map(Object.entries(initial));
  return {
    getItem(key) {
      return values.has(key) ? values.get(key) : null;
    },
    setItem(key, value) {
      values.set(key, String(value));
    },
    removeItem(key) {
      values.delete(key);
    },
    clear() {
      values.clear();
    }
  };
}

test('HttpClient DELETE never replays a write after a connection failure', async () => {
  const originalWindow = globalThis.window;
  const originalFetch = globalThis.fetch;
  const localStorage = createStorage();
  const sessionStorage = createStorage();
  const requests = [];

  globalThis.window = {
    location: {
      protocol: 'file:',
      hostname: '',
      port: '',
      href: 'file:///C:/ClearVision/index.html'
    },
    localStorage,
    sessionStorage
  };

  globalThis.fetch = async (url, options = {}) => {
    requests.push({ url: String(url), method: options.method });

    if (String(url) === 'http://localhost:5000/api/projects/project-1' && options.method === 'DELETE') {
      throw new TypeError('Failed to fetch');
    }

    return new Response('not found', { status: 404 });
  };

  try {
    const client = new HttpClient();
    await assert.rejects(() => client.delete('/projects/project-1'), /无法连接到 ClearVision 服务/);
    assert.equal(localStorage.getItem('cv_api_port'), null);
    assert.deepEqual(
      requests.map(request => `${request.method} ${request.url}`),
      ['DELETE http://localhost:5000/api/projects/project-1']
    );
  } finally {
    globalThis.window = originalWindow;
    globalThis.fetch = originalFetch;
  }
});

test('HttpClient flags 401 responses as auth errors and broadcasts the unauthorized signal', async () => {
  const originalWindow = globalThis.window;
  const originalFetch = globalThis.fetch;
  const originalCustomEvent = globalThis.CustomEvent;
  const localStorage = createStorage();
  const sessionStorage = createStorage({ cv_auth_token: 'expired-token' });
  const dispatched = [];

  class FakeCustomEvent {
    constructor(type, init = {}) {
      this.type = type;
      this.detail = init.detail ?? null;
    }
  }
  globalThis.CustomEvent = FakeCustomEvent;

  globalThis.window = {
    location: {
      protocol: 'file:',
      hostname: '',
      port: '',
      href: 'file:///C:/ClearVision/index.html'
    },
    localStorage,
    sessionStorage,
    dispatchEvent(event) {
      dispatched.push(event);
      return true;
    }
  };

  globalThis.fetch = async (url, options = {}) => {
    if (String(url) === 'http://localhost:5000/api/flows/preview-node' && options.method === 'POST') {
      return new Response(JSON.stringify({ error: 'Unauthorized', message: '请先登录' }), {
        status: 401,
        headers: { 'content-type': 'application/json' }
      });
    }

    return new Response('not found', { status: 404 });
  };

  try {
    const client = new HttpClient();
    let caught = null;
    try {
      await client.post('/flows/preview-node', { targetNodeId: 'node-1' });
    } catch (error) {
      caught = error;
    }

    assert.ok(caught instanceof HttpError, 'a 401 should surface as an HttpError');
    assert.equal(caught.status, 401);
    assert.equal(caught.isAuthError, true, '401 must be flagged as an auth error so previews do not render it as an operator failure');

    const authEvents = dispatched.filter(event => event.type === 'clearvision:auth-unauthorized');
    assert.equal(authEvents.length, 1, 'exactly one unauthorized signal should be broadcast');
    assert.equal(authEvents[0].detail.status, 401);
  } finally {
    globalThis.window = originalWindow;
    globalThis.fetch = originalFetch;
    globalThis.CustomEvent = originalCustomEvent;
  }
});
