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

function createClassList() {
  const values = new Set();
  return {
    add(...tokens) {
      tokens.filter(Boolean).forEach(token => values.add(token));
    },
    remove(...tokens) {
      tokens.filter(Boolean).forEach(token => values.delete(token));
    },
    contains(token) {
      return values.has(token);
    },
    toggle(token, force) {
      const enabled = force ?? !values.has(token);
      if (enabled) values.add(token);
      else values.delete(token);
      return enabled;
    }
  };
}

function createElementStub(tagName = 'div') {
  const listeners = new Map();
  const queryChildren = new Map();
  const element = {
    tagName: tagName.toUpperCase(),
    id: '',
    value: '',
    textContent: '',
    innerHTML: '',
    disabled: false,
    checked: false,
    dataset: {},
    style: {},
    children: [],
    classList: createClassList(),
    appendChild(child) {
      this.children.push(child);
      child.parentNode = this;
      return child;
    },
    remove() {},
    setAttribute(name, value) {
      this[name] = String(value);
    },
    removeAttribute(name) {
      delete this[name];
    },
    addEventListener(type, handler) {
      listeners.set(type, handler);
    },
    removeEventListener(type) {
      listeners.delete(type);
    },
    querySelector(selector) {
      if (selector === '.cv-toast-message' || selector === '.cv-toast-close') {
        if (!queryChildren.has(selector)) {
          queryChildren.set(selector, createElementStub(selector === '.cv-toast-close' ? 'button' : 'span'));
        }
        return queryChildren.get(selector);
      }
      return null;
    },
    querySelectorAll() {
      return [];
    }
  };
  return element;
}

const storage = createStorage();
const sessionStorage = createStorage();
const body = createElementStub('body');
const documentElement = createElementStub('html');

globalThis.document = {
  body,
  documentElement,
  createElement: createElementStub,
  getElementById() {
    return null;
  },
  querySelector() {
    return null;
  },
  addEventListener() {}
};

globalThis.window = {
  currentUser: null,
  location: {
    href: 'http://localhost/index.html',
    pathname: '/index.html',
    protocol: 'http:',
    hostname: 'localhost',
    port: '5000'
  },
  localStorage: storage,
  sessionStorage,
  setTimeout,
  clearTimeout,
  addEventListener() {},
  removeEventListener() {}
};
globalThis.confirm = () => true;
globalThis.prompt = () => null;

const authModule = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/auth/auth.js');
const {
  Capabilities,
  PermissionGuard,
  normalizeAuthenticatedContext
} = authModule;
const { SettingsView } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/settingsView.js');
const { StationMonitorView } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js');
const settingsApi = (await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/settingsApi.js')).default;

function assertButtonDisabled(html, id) {
  assert.match(html, new RegExp(`<button[^>]*id="${id}"[^>]*disabled[^>]*>`));
}

function createSettingsView() {
  const view = Object.create(SettingsView.prototype);
  view.config = view.getDefaultConfig();
  view.databaseStatus = {};
  view.diskUsage = null;
  view.aiModels = [];
  view.editingAiModelId = null;
  view.cameraBindings = [];
  view.selectedCameraBindingId = null;
  view.serialPhotoelectricPorts = [];
  view.serialPhotoelectricPortsLoaded = false;
  view.passwordPolicy = { minimumLength: 17 };
  view.renderScopeNotice = () => '';
  view.hasCapability = () => false;
  view.requireCapability = () => false;
  return view;
}

test('authenticated context normalization is deterministic and missing capabilities fail closed', () => {
  const normalized = normalizeAuthenticatedContext({
    userId: 'user-a',
    username: 'admin-named-user',
    role: 'Admin',
    capabilities: [' users.read ', 'project.edit', 'users.read', 42, ''],
    passwordPolicy: { minimumLength: '17' }
  });

  assert.deepEqual(normalized.capabilities, ['project.edit', 'users.read']);
  assert.deepEqual(normalized.passwordPolicy, { minimumLength: 17 });

  window.currentUser = {
    userId: 'admin-role-only',
    username: 'admin-role-only',
    role: 'Admin'
  };
  assert.equal(PermissionGuard.has(Capabilities.USERS_READ), false);
  assert.equal(PermissionGuard.canManageUsers(), false);
  assert.equal(PermissionGuard.canEdit(), false);

  window.currentUser = normalized;
  assert.equal(PermissionGuard.has(Capabilities.USERS_READ), true);
  assert.equal(PermissionGuard.canManageUsers(), true);
});

test('station production actions require their action capabilities at the handler boundary', async () => {
  const view = Object.create(StationMonitorView.prototype);
  view.selectedStationId = 'station-a';

  window.currentUser = { role: 'Admin', capabilities: [] };
  assert.equal(view.canPerformStationAction('ping'), false);
  assert.equal(view.canPerformStationAction('deploy'), false);
  assert.equal(view.canPerformStationAction('testDeploy'), false);
  await assert.rejects(() => view.createCommand('Ping', {}), /不能创建 Station 命令/);
  await assert.rejects(() => view.deployLatestPackage(), /不能部署 Station 运行包/);
  await assert.rejects(() => view.createAndDeployTestPackage(), /不能生成或下发 Station 测试包/);

  window.currentUser.capabilities = [
    Capabilities.STATION_COMMANDS_CREATE,
    Capabilities.STATION_PACKAGES_DEPLOY,
    Capabilities.STATION_TEST_PACKAGES_CREATE
  ];
  assert.equal(view.canPerformStationAction('ping'), true);
  assert.equal(view.canPerformStationAction('deploy'), true);
  assert.equal(view.canPerformStationAction('testDeploy'), true);
});

test('settings and database mutation controls render disabled without capabilities', () => {
  const view = createSettingsView();
  const generalHtml = view.renderGeneralTab();
  const databaseHtml = view.renderDatabaseTab();
  const usersHtml = view.renderUserManagementTab();
  const cameraHtml = view.renderCameraTab();

  assert.match(generalHtml, /minlength="17"/);
  assert.match(generalHtml, /当前密码策略：17 位/);
  assertButtonDisabled(generalHtml, 'btn-reset-settings');
  assertButtonDisabled(databaseHtml, 'btn-database-backup');
  assertButtonDisabled(databaseHtml, 'btn-database-refresh');
  assertButtonDisabled(databaseHtml, 'btn-database-repair');
  assertButtonDisabled(databaseHtml, 'btn-database-restore');
  assertButtonDisabled(databaseHtml, 'btn-database-cleanup');
  assertButtonDisabled(usersHtml, 'btn-add-user');
  assertButtonDisabled(cameraHtml, 'btn-discover-huaray-cameras');
  assertButtonDisabled(cameraHtml, 'btn-discover-hikvision-cameras');
  assertButtonDisabled(cameraHtml, 'btn-save-camera-params');
});

test('database maintenance handlers short-circuit before issuing requests', async () => {
  const view = createSettingsView();
  let requests = 0;
  const originals = {
    backupDatabase: settingsApi.backupDatabase,
    repairDatabase: settingsApi.repairDatabase,
    restoreDatabase: settingsApi.restoreDatabase,
    cleanupDatabaseHistory: settingsApi.cleanupDatabaseHistory
  };
  settingsApi.backupDatabase = async () => { requests += 1; };
  settingsApi.repairDatabase = async () => { requests += 1; };
  settingsApi.restoreDatabase = async () => { requests += 1; };
  settingsApi.cleanupDatabaseHistory = async () => { requests += 1; };

  try {
    await view.createDatabaseBackup();
    await view.runDatabaseRepair();
    await view.restoreDatabaseBackup();
    await view.cleanupDatabaseHistory();
  } finally {
    Object.assign(settingsApi, originals);
  }

  assert.equal(requests, 0);
});

test('AI mutation actions render disabled and update handler fails closed', async () => {
  const view = createSettingsView();
  const tbody = { innerHTML: '' };
  view.container = {
    querySelector(selector) {
      return selector === '#ai-models-table tbody' ? tbody : null;
    }
  };
  view.aiModels = [{
    id: 'model-a',
    name: 'Model A',
    provider: 'OpenAI',
    model: 'gpt-example',
    roleBindings: ['generation'],
    isEnabled: true,
    isActive: false
  }];
  view.refreshAiPerformanceOverview = () => {};

  view.refreshAiTableOnly();

  assert.match(tbody.innerHTML, /data-action="activate"[^>]*disabled/);
  assert.match(tbody.innerHTML, /data-action="default-planner"[^>]*disabled/);
  assert.match(tbody.innerHTML, /data-action="default-shadow-eval"[^>]*disabled/);
  assert.match(tbody.innerHTML, /data-action="delete"[^>]*disabled/);
  await assert.rejects(() => view._saveCurrentForm(), /不能更新 AI 模型/);
});

test('user creation and reset-password UI use the projected minimum length', async () => {
  const view = createSettingsView();
  view.passwordPolicy = { minimumLength: 19 };
  view.requireCapability = capability => [
    Capabilities.USERS_CREATE,
    Capabilities.USERS_RESET_PASSWORD
  ].includes(capability);
  view.lifecycle = { trackEvent: () => () => {} };

  let modalHtml = '';
  view.createTrackedModal = ({ content }) => {
    modalHtml = content.innerHTML;
    return {};
  };
  view.showUserModal('create', null);
  assert.match(modalHtml, /初始密码 \(至少19位\)/);
  assert.match(modalHtml, /id="modal-user-password" minlength="19"/);

  let clickHandler = null;
  const userTab = {
    addEventListener(type, handler) {
      if (type === 'click') clickHandler = handler;
    }
  };
  view.container = {
    querySelector(selector) {
      return selector === '[data-section="users"]' ? userTab : null;
    }
  };
  view.users = [{ id: 'user-b', username: 'operator-b', isActive: true, role: 'Operator' }];
  view.refreshUserTable = () => {};
  view.bindUserManagementEvents();

  let promptText = '';
  let resetPayload = null;
  const originalPrompt = globalThis.prompt;
  const originalReset = settingsApi.resetUserPassword;
  globalThis.prompt = message => {
    promptText = message;
    return 'x'.repeat(19);
  };
  settingsApi.resetUserPassword = async (id, payload) => {
    resetPayload = { id, payload };
  };

  try {
    await clickHandler({
      target: {
        closest() {
          return { id: '', dataset: { action: 'reset-pwd', id: 'user-b' } };
        }
      }
    });
  } finally {
    globalThis.prompt = originalPrompt;
    settingsApi.resetUserPassword = originalReset;
  }

  assert.match(promptText, /至少19位/);
  assert.deepEqual(resetPayload, {
    id: 'user-b',
    payload: { newPassword: 'x'.repeat(19) }
  });
});
