import test from 'node:test';
import assert from 'node:assert/strict';
import {
  LEGACY_LOCAL_DRAFT_KEY,
  LOCAL_DRAFT_SCHEMA,
  LOCAL_DRAFT_VERSION,
  LocalDraftStorage,
  buildLocalDraftStorageKey
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/project/localDraftStorage.js';

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
    },
    keys() {
      return [...values.keys()];
    }
  };
}

function createHarness(initialUser = { userId: 'user-a' }) {
  const storage = createStorage();
  let currentUser = initialUser;
  const drafts = new LocalDraftStorage({
    storageProvider: () => storage,
    userProvider: () => currentUser,
    nowProvider: () => '2026-08-29T12:34:56.000Z'
  });

  return {
    drafts,
    storage,
    setUser(user) {
      currentUser = user;
    }
  };
}

function createFlow(nodeId) {
  return {
    operators: [{ id: nodeId, parameters: { threshold: 123 } }],
    connections: []
  };
}

function validPayload({ userId = 'user-a', projectId = 'project-1', flow = createFlow('node-1') } = {}) {
  return {
    schema: LOCAL_DRAFT_SCHEMA,
    version: LOCAL_DRAFT_VERSION,
    userId,
    projectId,
    projectName: 'Project One',
    timestamp: '2026-08-29T12:34:56.000Z',
    source: 'timer',
    nodeCount: 1,
    flow
  };
}

test('logout and user switch cannot expose another user draft, while the owner can restore it later', () => {
  const { drafts, storage, setUser } = createHarness();
  const flowA = createFlow('private-node-a');

  const written = drafts.write({ id: 'shared-project', name: 'Shared' }, flowA);
  assert.equal(written.userId, 'user-a');
  assert.equal(written.projectId, 'shared-project');

  setUser(null);
  assert.equal(drafts.read('shared-project'), null);

  setUser({ userId: 'user-b' });
  assert.equal(drafts.read('shared-project'), null);
  assert.notEqual(storage.getItem(buildLocalDraftStorageKey('user-a', 'shared-project')), null);

  setUser({ userId: 'user-a' });
  assert.deepEqual(drafts.read('shared-project')?.flow, flowA);
});

test('one user has independent draft namespaces for different projects', () => {
  const { drafts } = createHarness();
  const firstFlow = createFlow('project-one-node');
  const secondFlow = createFlow('project-two-node');

  drafts.write({ id: 'project-1' }, firstFlow);
  drafts.write({ id: 'project-2' }, secondFlow);

  assert.deepEqual(drafts.read('project-1')?.flow, firstFlow);
  assert.deepEqual(drafts.read('project-2')?.flow, secondFlow);
});

test('ownerless legacy backup is ignored and deleted instead of being assigned to a user', () => {
  const { drafts, storage } = createHarness();
  storage.setItem(LEGACY_LOCAL_DRAFT_KEY, JSON.stringify({
    projectId: 'project-1',
    timestamp: '2026-08-28T00:00:00.000Z',
    flow: createFlow('legacy-node')
  }));

  assert.equal(drafts.read('project-1'), null);
  assert.equal(storage.getItem(LEGACY_LOCAL_DRAFT_KEY), null);
  assert.deepEqual(storage.keys(), []);
});

test('corrupt JSON fails closed and is removed from the current scoped key', () => {
  const { drafts, storage } = createHarness();
  const key = buildLocalDraftStorageKey('user-a', 'project-1');
  storage.setItem(key, '{not-valid-json');

  assert.equal(drafts.read('project-1'), null);
  assert.equal(storage.getItem(key), null);
});

test('payload owner, project, schema, version, flow and timestamp are all verified', () => {
  const invalidPayloads = [
    { ...validPayload(), userId: 'user-b' },
    { ...validPayload(), projectId: 'project-2' },
    { ...validPayload(), schema: 'unknown-schema' },
    { ...validPayload(), version: LOCAL_DRAFT_VERSION + 1 },
    { ...validPayload(), flow: null },
    { ...validPayload(), timestamp: 'not-a-timestamp' }
  ];

  for (const payload of invalidPayloads) {
    const { drafts, storage } = createHarness();
    const key = buildLocalDraftStorageKey('user-a', 'project-1');
    storage.setItem(key, JSON.stringify(payload));

    assert.equal(drafts.read('project-1'), null);
    assert.equal(storage.getItem(key), null);
  }
});

test('successful clear removes only the current user and project draft', () => {
  const { drafts, storage, setUser } = createHarness();
  const userAProjectOne = createFlow('a-project-one');
  const userAProjectTwo = createFlow('a-project-two');
  const userBProjectOne = createFlow('b-project-one');

  drafts.write({ id: 'project-1' }, userAProjectOne);
  drafts.write({ id: 'project-2' }, userAProjectTwo);
  setUser({ userId: 'user-b' });
  drafts.write({ id: 'project-1' }, userBProjectOne);

  setUser({ userId: 'user-a' });
  assert.equal(drafts.clear('project-1'), true);
  assert.equal(storage.getItem(buildLocalDraftStorageKey('user-a', 'project-1')), null);
  assert.deepEqual(drafts.read('project-2')?.flow, userAProjectTwo);

  setUser({ userId: 'user-b' });
  assert.deepEqual(drafts.read('project-1')?.flow, userBProjectOne);
});

test('missing or bootstrap-incomplete authenticated user prevents both read and write', () => {
  const { drafts, storage, setUser } = createHarness(null);
  const key = buildLocalDraftStorageKey('user-a', 'project-1');
  storage.setItem(key, JSON.stringify(validPayload()));

  assert.equal(drafts.read('project-1'), null);
  assert.equal(drafts.write({ id: 'project-1' }, createFlow('unauthenticated-node')), null);
  assert.notEqual(storage.getItem(key), null);

  setUser({ username: 'user-a', role: 'Admin' });
  assert.equal(drafts.read('project-1'), null);
  assert.equal(drafts.write({ id: 'project-1' }, createFlow('missing-id-node')), null);

  setUser({ userId: 'user-a' });
  assert.deepEqual(drafts.read('project-1')?.flow, createFlow('node-1'));
});

test('storage keys and payloads bind only stable IDs and never session secrets', () => {
  const { drafts, storage } = createHarness({
    userId: 'user:a@example.test',
    username: 'Visible Name',
    token: 'must-not-be-persisted'
  });
  const key = buildLocalDraftStorageKey('user:a@example.test', 'project/with spaces');

  const payload = drafts.write({ id: 'project/with spaces' }, createFlow('node-secret-check'));

  assert.match(key, /^cv_local_draft:v1:/);
  assert.equal(key.includes('must-not-be-persisted'), false);
  assert.equal(payload.userId, 'user:a@example.test');
  assert.equal(Object.hasOwn(payload, 'token'), false);
  assert.equal(storage.getItem(key).includes('must-not-be-persisted'), false);
});
