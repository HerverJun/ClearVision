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

function createConnectionRefusedError() {
  const error = new TypeError('net::ERR_CONNECTION_REFUSED');
  error.cause = { code: 'ECONNREFUSED' };
  return error;
}

test('HttpClient DELETE rediscovers backend port and retries after WebView connection failure', async () => {
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
      throw createConnectionRefusedError();
    }

    if (String(url) === 'http://localhost:5000/health') {
      return new Response('not found', { status: 404 });
    }

    if (String(url) === 'http://localhost:5001/health') {
      return new Response('ok', { status: 200 });
    }

    if (String(url) === 'http://localhost:5001/api/projects/project-1' && options.method === 'DELETE') {
      return new Response(JSON.stringify({ deleted: true }), {
        status: 200,
        headers: { 'content-type': 'application/json' }
      });
    }

    return new Response('not found', { status: 404 });
  };

  try {
    const client = new HttpClient();
    const result = await client.delete('/projects/project-1');

    assert.deepEqual(result, { deleted: true });
    assert.equal(localStorage.getItem('cv_api_port'), '5001');
    assert.deepEqual(
      requests.map(request => `${request.method} ${request.url}`),
      [
        'DELETE http://localhost:5000/api/projects/project-1',
        'GET http://localhost:5000/health',
        'GET http://localhost:5001/health',
        'DELETE http://localhost:5001/api/projects/project-1'
      ]
    );
  } finally {
    globalThis.window = originalWindow;
    globalThis.fetch = originalFetch;
  }
});

test('HttpClient retries every supported verb once from a stale saved port while preserving request state', async () => {
  const originalWindow = globalThis.window;
  const originalFetch = globalThis.fetch;
  const localStorage = createStorage();
  const sessionStorage = createStorage({ cv_auth_token: 'operator-token' });
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
    const request = {
      url: String(url),
      method: options.method,
      body: options.body,
      headers: { ...options.headers },
      signal: options.signal
    };
    requests.push(request);

    if (request.url === 'http://localhost:5000/health') {
      return new Response('ok', { status: 200 });
    }

    if (request.url.startsWith('http://localhost:5001/api/recovery/')) {
      throw createConnectionRefusedError();
    }

    if (request.url.startsWith('http://localhost:5000/api/recovery/')) {
      return new Response(JSON.stringify({ recovered: request.method }), {
        status: 200,
        headers: { 'content-type': 'application/json' }
      });
    }

    return new Response('not found', { status: 404 });
  };

  try {
    const operations = [
      {
        method: 'GET',
        path: '/recovery/get',
        invoke: (client, options) => client.get('/recovery/get', { source: 'saved-port' }, options),
        expectedBody: undefined
      },
      {
        method: 'POST',
        path: '/recovery/post',
        invoke: (client, options) => client.post('/recovery/post', { value: 'post' }, options),
        expectedBody: JSON.stringify({ value: 'post' })
      },
      {
        method: 'PUT',
        path: '/recovery/put',
        invoke: (client, options) => client.put('/recovery/put', { value: 'put' }, options),
        expectedBody: JSON.stringify({ value: 'put' })
      },
      {
        method: 'PATCH',
        path: '/recovery/patch',
        invoke: (client, options) => client.patch('/recovery/patch', { value: 'patch' }, options),
        expectedBody: JSON.stringify({ value: 'patch' })
      },
      {
        method: 'DELETE',
        path: '/recovery/delete',
        invoke: (client, options) => client.delete('/recovery/delete', options),
        expectedBody: undefined
      }
    ];

    for (const operation of operations) {
      const start = requests.length;
      const controller = new AbortController();
      localStorage.setItem('cv_api_port', '5001');
      const client = new HttpClient();
      const options = {
        headers: { 'X-Recovery-Test': operation.method },
        signal: controller.signal
      };

      const result = await operation.invoke(client, options);
      assert.deepEqual(result, { recovered: operation.method });
      assert.equal(localStorage.getItem('cv_api_port'), '5000');

      const operationRequests = requests.slice(start).filter(request => request.url.includes(operation.path));
      assert.equal(operationRequests.length, 2, `${operation.method} should be sent exactly once before and once after recovery`);
      for (const request of operationRequests) {
        assert.equal(request.headers.Authorization, 'Bearer operator-token');
        assert.equal(request.headers['X-Recovery-Test'], operation.method);
        assert.equal(request.signal, controller.signal);
        assert.equal(request.body, operation.expectedBody);
      }
    }
  } finally {
    globalThis.window = originalWindow;
    globalThis.fetch = originalFetch;
  }
});

test('HttpClient does not ambiguously replay a mutation after a generic transport failure', async () => {
  const originalWindow = globalThis.window;
  const originalFetch = globalThis.fetch;
  const localStorage = createStorage({ cv_api_port: '5001' });
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
    throw new TypeError('Failed to fetch');
  };

  try {
    const client = new HttpClient();
    await assert.rejects(client.post('/orders', { orderId: 'o-1' }));
    assert.deepEqual(requests, [
      { url: 'http://localhost:5001/api/orders', method: 'POST' }
    ], 'a mutation without an idempotency key must not start discovery or be replayed');
  } finally {
    globalThis.window = originalWindow;
    globalThis.fetch = originalFetch;
  }
});

test('HttpClient can recover an idempotent mutation but never retries an HTTP response', async () => {
  const originalWindow = globalThis.window;
  const originalFetch = globalThis.fetch;
  const localStorage = createStorage({ cv_api_port: '5001' });
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
    requests.push({ url: String(url), method: options.method, headers: { ...options.headers } });
    if (String(url) === 'http://localhost:5001/api/orders') {
      throw new TypeError('Failed to fetch');
    }
    if (String(url) === 'http://localhost:5000/health') {
      return new Response('ok', { status: 200 });
    }
    if (String(url) === 'http://localhost:5000/api/orders') {
      return new Response(JSON.stringify({ accepted: true }), {
        status: 200,
        headers: { 'content-type': 'application/json' }
      });
    }
    return new Response('not found', { status: 404 });
  };

  try {
    const client = new HttpClient();
    const result = await client.post('/orders', { orderId: 'o-2' }, { idempotencyKey: 'request-2' });
    assert.deepEqual(result, { accepted: true });
    assert.deepEqual(requests.map(request => `${request.method} ${request.url}`), [
      'POST http://localhost:5001/api/orders',
      'GET http://localhost:5000/health',
      'POST http://localhost:5000/api/orders'
    ]);
    assert.equal(requests.at(-1).headers['Idempotency-Key'], 'request-2');

    requests.length = 0;
    localStorage.setItem('cv_api_port', '5001');
    globalThis.fetch = async (url, options = {}) => {
      requests.push({ url: String(url), method: options.method });
      return new Response('gateway timeout', { status: 504 });
    };

    await assert.rejects(client.post('/orders', { orderId: 'o-3' }, { idempotencyKey: 'request-3' }), HttpError);
    assert.deepEqual(requests, [
      { url: 'http://localhost:5001/api/orders', method: 'POST' }
    ], 'an HTTP response means the request reached a server and must not be replayed');
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
