import test from 'node:test';
import assert from 'node:assert/strict';
import { HttpClient } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/httpClient.js';

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
      throw new TypeError('Failed to fetch');
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
