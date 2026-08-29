import test from 'node:test';
import assert from 'node:assert/strict';

function createStorage() {
  const values = new Map();
  return {
    getItem(key) {
      return values.has(key) ? values.get(key) : null;
    },
    setItem(key, value) {
      values.set(key, String(value));
    },
    removeItem(key) {
      values.delete(key);
    }
  };
}

const sessionStorage = createStorage();
const localStorage = createStorage();
let href = 'http://localhost/index.html';
let userObservedAtRedirect = undefined;
const location = {
  pathname: '/index.html',
  protocol: 'http:',
  hostname: 'localhost',
  port: '5000',
  get href() {
    return href;
  },
  set href(value) {
    userObservedAtRedirect = globalThis.window.currentUser;
    href = value;
  }
};

globalThis.window = {
  __API_BASE_URL__: 'http://localhost/api',
  currentUser: null,
  location,
  sessionStorage,
  localStorage
};

const auth = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/auth/auth.js');
const authStorage = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/auth/authStorage.js');
const httpClient = (await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/httpClient.js')).default;

test('auth context notifies only on stable user ID changes and logout clears memory before redirect', async (t) => {
  const originalFetch = globalThis.fetch;
  const originalPost = httpClient.post;
  const responses = [
    {
      userId: 'user-a',
      username: 'alice',
      role: 'Engineer',
      capabilities: ['project.edit'],
      passwordPolicy: { minimumLength: 12 }
    },
    {
      userId: 'user-a',
      username: 'alice-renamed',
      role: 'Engineer',
      capabilities: ['project.edit', 'cameras.capture'],
      passwordPolicy: { minimumLength: 14 }
    },
    {
      userId: 'user-b',
      username: 'bob',
      role: 'Operator',
      capabilities: [],
      passwordPolicy: { minimumLength: 14 }
    }
  ];
  const transitions = [];
  const unsubscribe = auth.subscribeAuthContext((nextUser, previousUser) => {
    transitions.push([
      previousUser?.userId ?? null,
      nextUser?.userId ?? null
    ]);
  });

  t.after(() => {
    unsubscribe();
    globalThis.fetch = originalFetch;
    httpClient.post = originalPost;
    authStorage.clearAuthSession();
  });

  authStorage.storeAuthSession('test-token', { userId: 'stored-user' });
  globalThis.fetch = async () => ({
    ok: true,
    async json() {
      return responses.shift();
    }
  });

  assert.equal((await auth.bootstrapAuthSession({ redirectOnFailure: false })).ok, true);
  assert.deepEqual(transitions, [[null, 'user-a']]);

  assert.equal((await auth.bootstrapAuthSession({ redirectOnFailure: false })).ok, true);
  assert.deepEqual(transitions, [[null, 'user-a']]);
  assert.equal(auth.getCurrentUser().username, 'alice-renamed');

  assert.equal((await auth.bootstrapAuthSession({ redirectOnFailure: false })).ok, true);
  assert.deepEqual(transitions, [
    [null, 'user-a'],
    ['user-a', 'user-b']
  ]);

  httpClient.post = async () => ({ ok: true });
  await auth.logout();

  assert.deepEqual(transitions, [
    [null, 'user-a'],
    ['user-a', 'user-b'],
    ['user-b', null]
  ]);
  assert.equal(auth.getCurrentUser(), null);
  assert.equal(authStorage.getStoredToken(), null);
  assert.equal(userObservedAtRedirect, null);
  assert.match(location.href, /login\.html$/);
});
