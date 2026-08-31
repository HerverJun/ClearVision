import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

function createClassList() {
  const values = new Set();
  return {
    add(...tokens) {
      tokens.filter(Boolean).forEach(token => values.add(token));
    },
    remove(...tokens) {
      tokens.filter(Boolean).forEach(token => values.delete(token));
    },
    toggle(token, force) {
      const shouldAdd = force ?? !values.has(token);
      if (shouldAdd) {
        values.add(token);
      } else {
        values.delete(token);
      }
      return shouldAdd;
    },
    contains(token) {
      return values.has(token);
    }
  };
}

function createElementStub(tagName = 'div') {
  const element = {
    tagName: tagName.toUpperCase(),
    id: '',
    className: '',
    value: '',
    disabled: false,
    textContent: '',
    innerHTML: '',
    parentNode: null,
    children: [],
    dataset: {},
    style: {},
    classList: createClassList(),
    attributes: new Map(),
    appendChild(child) {
      child.parentNode = element;
      element.children.push(child);
      return child;
    },
    removeChild(child) {
      element.children = element.children.filter(item => item !== child);
      child.parentNode = null;
    },
    remove() {
      element.parentNode?.removeChild?.(element);
    },
    setAttribute(name, value) {
      element.attributes.set(name, String(value));
    },
    removeAttribute(name) {
      element.attributes.delete(name);
    },
    addEventListener() {},
    querySelector(selector) {
      if (selector === '.plc-test-label') {
        return element.plcTestLabel || null;
      }
      if (selector === '.cv-toast-message' || selector === '.cv-toast-close') {
        const child = createElementStub(selector === '.cv-toast-close' ? 'button' : 'span');
        element.appendChild(child);
        return child;
      }
      return null;
    },
    querySelectorAll() {
      return [];
    }
  };
  return element;
}

function installDom() {
  const body = createElementStub('body');
  const containers = new Map();

  globalThis.document = {
    body,
    createElement: createElementStub,
    addEventListener() {},
    getElementById(id) {
      return containers.get(id) || null;
    },
    querySelector() {
      return null;
    }
  };

  globalThis.window = {
    currentUser: { role: 'Admin' },
    location: { protocol: 'http:', hostname: '127.0.0.1', port: '5000' },
    localStorage: createStorage(),
    sessionStorage: createStorage()
  };

  return {
    register(id, element) {
      containers.set(id, element);
    }
  };
}

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

const dom = installDom();
const { SettingsView } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/settingsView.js');
const { Capabilities } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/auth/auth.js');
const settingsApi = (await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/settingsApi.js')).default;
const httpClient = (await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/httpClient.js')).default;

function createField(value = '') {
  return { value: String(value) };
}

function createButton() {
  const button = createElementStub('button');
  button.plcTestLabel = createElementStub('span');
  button.querySelector = selector => selector === '.plc-test-label' ? button.plcTestLabel : null;
  return button;
}

function createBadge() {
  const badge = createElementStub('div');
  return badge;
}

function createRow(fields) {
  return {
    querySelector(selector) {
      const match = selector.match(/^\[data-field="([^"]+)"\]$/);
      if (!match) return null;
      return fields[match[1]] || null;
    }
  };
}

function createContainer({ fields = {}, rows = [], button = null, badge = null } = {}) {
  const elements = new Map(Object.entries({
    '#cfg-protocol': fields.protocol || createField('S7'),
    '#cfg-plcIpAddress': fields.ipAddress || createField(''),
    '#cfg-plcPort': fields.port || createField(''),
    '#cfg-s7-cpuType': fields.cpuType || createField('S7-1200'),
    '#cfg-s7-rack': fields.rack || createField('0'),
    '#cfg-s7-slot': fields.slot || createField('1'),
    '#btn-plc-test': button,
    '#plc-connection-badge': badge
  }).filter(([, value]) => value));

  return {
    dataset: {},
    querySelector(selector) {
      return elements.get(selector) || null;
    },
    querySelectorAll(selector) {
      return selector === '#plc-mapping-tbody tr.plc-mapping-row' ? rows : [];
    },
    addEventListener() {}
  };
}

function baseCommunication() {
  return {
    activeProtocol: 'S7',
    heartbeatIntervalMs: 1000,
    s7: {
      ipAddress: '192.168.0.1',
      port: 102,
      cpuType: 'S7-1200',
      rack: 0,
      slot: 1,
      mappings: [{ name: 'SavedS7', address: 'DB1.DBX0.0', dataType: 'Bool', description: '', canWrite: false }]
    },
    mc: {
      ipAddress: '192.168.3.1',
      port: 5002,
      mappings: [{ name: 'SavedMc', address: 'D100', dataType: 'Word', description: '', canWrite: false }]
    },
    fins: {
      ipAddress: '192.168.250.1',
      port: 9600,
      mappings: [{ name: 'SavedFins', address: 'DM100', dataType: 'Word', description: '', canWrite: false }]
    }
  };
}

function createView(communication = baseCommunication()) {
  globalThis.window.currentUser = {
    userId: 'admin-id',
    username: 'admin-user',
    role: 'Admin',
    capabilities: Object.values(Capabilities),
    passwordPolicy: { minimumLength: 12 }
  };
  const root = createContainer();
  dom.register('settings-view', root);
  const view = new SettingsView('settings-view');
  view.config = view.normalizeAppConfig({ communication });
  view.config.revision = 0;
  view.savedCommunicationConfig = view.cloneCommunicationConfig(view.config.communication);
  view.plcProfileDrafts = {};
  view.plcValidationErrors = [];
  view.refreshCommunicationPanel = () => {};
  view.renderPlcMappingsTable = () => {};
  return view;
}

function mappingRow({ name = '', address = '', dataType = 'Bool', description = '', canWrite = 'false' }) {
  return createRow({
    name: createField(name),
    address: createField(address),
    dataType: createField(dataType),
    description: createField(description),
    canWrite: createField(canWrite)
  });
}

test('PLC protocol drafts stay isolated across S7, MC, and FINS switches', () => {
  const view = createView();

  view.container = createContainer({
    fields: {
      ipAddress: createField('10.0.0.7'),
      port: createField('1102'),
      cpuType: createField('S7-1500'),
      rack: createField('2'),
      slot: createField('3')
    },
    rows: [mappingRow({ name: 'S7Start', address: 'DB1.DBX0.0', dataType: 'Bool', description: 's7', canWrite: 'true' })]
  });
  view.syncActivePlcProfileDraft('S7');

  view.config.communication.activeProtocol = 'MC';
  view.container = createContainer({
    fields: {
      ipAddress: createField('10.0.0.8'),
      port: createField('5008')
    },
    rows: [mappingRow({ name: 'McReady', address: 'D100', dataType: 'Word', description: 'mc' })]
  });
  view.syncActivePlcProfileDraft('MC');

  view.config.communication.activeProtocol = 'FINS';
  view.container = createContainer({
    fields: {
      ipAddress: createField('10.0.0.9'),
      port: createField('9609')
    },
    rows: [mappingRow({ name: 'FinsDone', address: 'DM100', dataType: 'Word', description: 'fins' })]
  });
  view.syncActivePlcProfileDraft('FINS');

  assert.equal(view.plcProfileDrafts.s7.ipAddress, '10.0.0.7');
  assert.equal(view.plcProfileDrafts.s7.port, 1102);
  assert.equal(view.plcProfileDrafts.s7.cpuType, 'S7-1500');
  assert.equal(view.plcProfileDrafts.s7.rack, 2);
  assert.equal(view.plcProfileDrafts.s7.slot, 3);
  assert.equal(view.plcProfileDrafts.s7.mappings[0].name, 'S7Start');
  assert.equal(view.plcProfileDrafts.s7.mappings[0].canWrite, true);
  assert.equal(view.plcProfileDrafts.mc.ipAddress, '10.0.0.8');
  assert.equal(view.plcProfileDrafts.mc.mappings[0].address, 'D100');
  assert.equal(view.plcProfileDrafts.fins.ipAddress, '10.0.0.9');
  assert.equal(view.plcProfileDrafts.fins.mappings[0].address, 'DM100');
});

test('PLC current-protocol save payload excludes unsaved drafts from other protocols', () => {
  const view = createView();

  view.container = createContainer({
    fields: {
      ipAddress: createField('10.10.10.11'),
      port: createField('1102'),
      cpuType: createField('S7-1500'),
      rack: createField('1'),
      slot: createField('2')
    }
  });
  view.syncActivePlcProfileDraft('S7');

  view.config.communication.activeProtocol = 'MC';
  view.container = createContainer({
    fields: {
      ipAddress: createField('10.10.10.22'),
      port: createField('5003')
    },
    rows: [mappingRow({ name: 'McCurrent', address: 'D200', dataType: 'Word', canWrite: 'true' })]
  });

  const currentProtocolPayload = view.buildPlcSettingsPayload();
  assert.equal(currentProtocolPayload.activeProtocol, 'MC');
  assert.equal(currentProtocolPayload.mc.ipAddress, '10.10.10.22');
  assert.equal(currentProtocolPayload.mc.port, 5003);
  assert.equal(currentProtocolPayload.mc.mappings[0].name, 'McCurrent');
  assert.equal(currentProtocolPayload.s7.ipAddress, '192.168.0.1');
  assert.equal(currentProtocolPayload.s7.port, 102);
  assert.equal(currentProtocolPayload.s7.cpuType, 'S7-1200');

  const allProfilesPayload = view.buildPlcSettingsPayload({ persistAllProfiles: true });
  assert.equal(allProfilesPayload.s7.ipAddress, '10.10.10.11');
  assert.equal(allProfilesPayload.s7.port, 1102);
  assert.equal(allProfilesPayload.mc.ipAddress, '10.10.10.22');
});

test('PLC connection test sends only the persisted profile id and restores loading state', async () => {
  const view = createView({
    ...baseCommunication(),
    activeProtocol: 'MC'
  });
  const button = createButton();
  const badge = createBadge();
  const capturedPayloads = [];
  const originalTestConnection = settingsApi.testPlcConnection;

  view.container = createContainer({
    fields: {
      ipAddress: createField('10.20.30.40'),
      port: createField('5010')
    },
    button,
    badge
  });

  settingsApi.testPlcConnection = async payload => {
    capturedPayloads.push(payload);
    assert.equal(button.disabled, true);
    assert.equal(button.plcTestLabel.textContent, '测试中...');
    return { success: true, message: '连接成功。' };
  };

  try {
    await view.testPlcConnection();
  } finally {
    settingsApi.testPlcConnection = originalTestConnection;
  }

  assert.deepEqual(capturedPayloads, [{ profileId: 'MC' }]);
  assert.equal(button.disabled, false);
  assert.equal(button.plcTestLabel.textContent, '连接测试');
  assert.equal(view.plcConnectionStatus, 'connected');
});

test('PLC hardware capability permits connection test but not Admin-only persistence', async () => {
  const view = createView({
    ...baseCommunication(),
    activeProtocol: 'MC'
  });
  const button = createButton();
  const badge = createBadge();
  const originalTestConnection = settingsApi.testPlcConnection;
  const originalSave = settingsApi.savePlcSettings;
  let testRequests = 0;
  let saveRequests = 0;

  globalThis.window.currentUser.capabilities = [Capabilities.PLC_CONNECTION_TEST];
  view.container = createContainer({
    fields: {
      ipAddress: createField('10.20.30.40'),
      port: createField('5010')
    },
    button,
    badge
  });
  settingsApi.testPlcConnection = async () => {
    testRequests += 1;
    return { success: true, message: '连接成功。' };
  };
  settingsApi.savePlcSettings = async () => {
    saveRequests += 1;
    return { success: true };
  };

  try {
    await view.testPlcConnection();
    const saveResult = await view.savePlcSettings({ silent: true });
    assert.equal(saveResult.success, false);
  } finally {
    settingsApi.testPlcConnection = originalTestConnection;
    settingsApi.savePlcSettings = originalSave;
  }

  assert.equal(testRequests, 1);
  assert.equal(saveRequests, 0);
});

test('PLC mapping add, delete, edit, type, write permission, and blank filtering enter payload', () => {
  const view = createView();
  view.plcMappings = [];

  view.addPlcMapping();
  assert.equal(view.plcMappings.length, 1);

  view.updatePlcMappingField(0, 'name', createField('StartFlag'));
  view.updatePlcMappingField(0, 'address', createField('DB1.DBX0.0'));
  view.updatePlcMappingField(0, 'dataType', createField('Bool'));
  view.updatePlcMappingField(0, 'canWrite', createField('true'));
  assert.equal(view.plcMappings[0].canWrite, true);

  view.addPlcMapping();
  view.deletePlcMapping(1);
  assert.equal(view.plcMappings.length, 1);

  view.container = createContainer({
    fields: {
      ipAddress: createField('192.168.0.20'),
      port: createField('102'),
      cpuType: createField('S7-1200'),
      rack: createField('0'),
      slot: createField('1')
    },
    rows: [
      mappingRow({ name: 'StartFlag', address: 'DB1.DBX0.0', dataType: 'Bool', description: 'start', canWrite: 'true' }),
      mappingRow({}),
      mappingRow({ name: 'Count', address: 'DB1.DBD4', dataType: 'Int32', description: 'count', canWrite: 'false' })
    ]
  });

  const payload = view.buildPlcSettingsPayload();
  assert.equal(payload.s7.mappings.length, 2);
  assert.deepEqual(payload.s7.mappings.map(item => ({
    name: item.name,
    address: item.address,
    dataType: item.dataType,
    canWrite: item.canWrite
  })), [
    { name: 'StartFlag', address: 'DB1.DBX0.0', dataType: 'Bool', canWrite: true },
    { name: 'Count', address: 'DB1.DBD4', dataType: 'Int32', canWrite: false }
  ]);
});

test('PLC validation errors land on the active protocol, field, and mapping index', () => {
  const view = createView({
    ...baseCommunication(),
    activeProtocol: 'MC'
  });

  view.plcValidationErrors = [
    { Protocol: 'S7', Section: 'connection', Field: 'ipAddress', Message: 'S7 IP 错误' },
    { protocol: 'MC', section: 'connection', field: 'port', message: 'MC 端口错误' },
    { protocol: 'MC', section: 'mapping', field: 'address', index: 1, message: 'MC 地址错误' },
    { protocol: 'FINS', section: 'mapping', field: 'name', index: 1, message: 'FINS 名称错误' }
  ];

  assert.deepEqual(view.getPlcFieldErrors('connection', 'port').map(error => error.message), ['MC 端口错误']);
  assert.deepEqual(view.getPlcFieldErrors('mapping', 'address', 1).map(error => error.message), ['MC 地址错误']);
  assert.deepEqual(view.getPlcFieldErrors('mapping', 'address', 0), []);
  assert.deepEqual(view.getPlcFieldErrors('connection', 'ipAddress'), []);
});

test('PLC save failure normalizes backend validation errors without marking draft clean', async () => {
  const view = createView({
    ...baseCommunication(),
    activeProtocol: 'FINS'
  });
  const originalSave = settingsApi.savePlcSettings;

  view.container = createContainer({
    fields: {
      ipAddress: createField('10.30.40.50'),
      port: createField('9600')
    },
    rows: [mappingRow({ name: 'BadFins', address: 'BAD', dataType: 'Word' })]
  });
  view.plcDraftDirty = true;

  settingsApi.savePlcSettings = async payload => ({
    success: false,
    message: 'PLC 配置校验失败。',
    settings: payload,
    errors: [
      { Protocol: 'FINS', Section: 'mapping', Field: 'address', Index: 0, Message: 'PLC 地址格式无效。' }
    ]
  });

  try {
    const result = await view.savePlcSettings({ silent: true });
    assert.equal(result.success, false);
  } finally {
    settingsApi.savePlcSettings = originalSave;
  }

  assert.equal(view.plcDraftDirty, true);
  assert.deepEqual(view.getPlcFieldErrors('mapping', 'address', 0).map(error => error.message), ['PLC 地址格式无效。']);
});

test('PLC save sends expected AppConfig revision, advances it once, and never performs a full settings write', async () => {
  const view = createView();
  view.config.revision = 17;
  view.container = createContainer({
    fields: {
      ipAddress: createField('192.168.0.22'),
      port: createField('102'),
      cpuType: createField('S7-1200'),
      rack: createField('0'),
      slot: createField('1')
    }
  });
  const originalSavePlc = settingsApi.savePlcSettings;
  const originalSaveSettings = settingsApi.saveSettings;
  let fullSettingsWrites = 0;
  let capturedPayload = null;
  settingsApi.saveSettings = async () => {
    fullSettingsWrites += 1;
    return {};
  };
  settingsApi.savePlcSettings = async payload => {
    capturedPayload = payload;
    return { success: true, settings: payload, revision: 18, errors: [] };
  };

  try {
    const result = await view.savePlcSettings({ silent: true });
    assert.equal(result.success, true);
  } finally {
    settingsApi.savePlcSettings = originalSavePlc;
    settingsApi.saveSettings = originalSaveSettings;
  }

  assert.equal(capturedPayload.expectedRevision, 17);
  assert.equal(view.config.revision, 18);
  assert.equal(fullSettingsWrites, 0);
});

test('PLC 409 refreshes only authoritative revision and preserves the active draft for retry', async () => {
  const view = createView();
  view.config.revision = 4;
  view.plcDraftDirty = true;
  const draftIp = createField('10.66.77.88');
  view.container = createContainer({
    fields: {
      ipAddress: draftIp,
      port: createField('102'),
      cpuType: createField('S7-1200'),
      rack: createField('0'),
      slot: createField('1')
    }
  });
  const originalSave = settingsApi.savePlcSettings;
  const originalLoad = settingsApi.loadSettings;
  settingsApi.savePlcSettings = async () => {
    const error = new Error('revision conflict');
    error.status = 409;
    throw error;
  };
  settingsApi.loadSettings = async () => ({ revision: 9, general: { softwareTitle: 'Other writer' } });

  let result;
  try {
    result = await view.savePlcSettings({ silent: true });
  } finally {
    settingsApi.savePlcSettings = originalSave;
    settingsApi.loadSettings = originalLoad;
  }

  assert.equal(result.conflict, true);
  assert.equal(view.config.revision, 9);
  assert.equal(view.plcDraftDirty, true);
  assert.equal(draftIp.value, '10.66.77.88');
  assert.equal(view.config.general.softwareTitle, 'ClearVision');
});

test('camera and TCP saves propagate AppConfig revision and refresh the local token', async () => {
  const view = createView();
  view.config.revision = 23;
  view.cameraBindings = [{
    id: 'cam-a',
    displayName: 'Camera A',
    serialNumber: 'SN-A',
    exposureTimeUs: 5000,
    gainDb: 0,
    triggerMode: 'Software'
  }];
  view.config.activeCameraId = 'cam-a';
  view.syncActiveCameraSelection = () => {};
  view.buildTcpProfilesPayload = () => [{
    id: 'robot',
    name: 'Robot',
    enabled: true,
    mode: 'Client',
    remoteHost: '127.0.0.1',
    remotePort: 9000,
    localHost: '127.0.0.1',
    localPort: 9001,
    encoding: 'UTF8',
    frameMode: 'Raw',
    lineEnding: 'None',
    timeoutMs: 5000,
    reconnect: true
  }];
  view.refreshTcpPanel = () => {};
  const originalCameraSave = settingsApi.saveCameraBindings;
  const originalTcpSave = settingsApi.saveTcpProfiles;
  let cameraPayload = null;
  let tcpRevision = null;
  settingsApi.saveCameraBindings = async payload => {
    cameraPayload = payload;
    return { revision: 24, bindings: payload.bindings, activeCameraId: payload.activeCameraId };
  };
  settingsApi.saveTcpProfiles = async (profiles, expectedRevision) => {
    tcpRevision = expectedRevision;
    return { success: true, profiles, revision: 25 };
  };

  try {
    assert.equal(await view.saveCameraBindings({ silent: true }), true);
    assert.equal(cameraPayload.expectedRevision, 23);
    assert.equal(view.config.revision, 24);
    const tcpResult = await view.saveTcpSettings({ silent: true });
    assert.equal(tcpResult.success, true);
  } finally {
    settingsApi.saveCameraBindings = originalCameraSave;
    settingsApi.saveTcpProfiles = originalTcpSave;
  }

  assert.equal(tcpRevision, 24);
  assert.equal(view.config.revision, 25);
});

test('settings API reset and TCP wrappers preserve the expected revision in request bodies', async () => {
  const originalPost = httpClient.post;
  const originalPut = httpClient.put;
  const calls = [];
  httpClient.post = async (url, payload) => {
    calls.push({ method: 'POST', url, payload });
    return { revision: 31 };
  };
  httpClient.put = async (url, payload) => {
    calls.push({ method: 'PUT', url, payload });
    return { success: true, profiles: payload.profiles, revision: 32 };
  };

  try {
    await settingsApi.resetSettings(30);
    await settingsApi.saveTcpProfiles([{ id: 'robot' }], 31);
  } finally {
    httpClient.post = originalPost;
    httpClient.put = originalPut;
  }

  assert.deepEqual(calls, [
    { method: 'POST', url: '/settings/reset', payload: { expectedRevision: 30 } },
    { method: 'PUT', url: '/tcp/profiles', payload: { profiles: [{ id: 'robot' }], expectedRevision: 31 } }
  ]);
});

test('all AppConfig UI mutation sources carry revision CAS and expose 409 retry handling', () => {
  const source = relativePath => readFileSync(new URL(relativePath, import.meta.url), 'utf8');
  const settingsView = source('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/settingsView.js');
  const systemTabs = source('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/tabs/systemTabs.js');
  const plcTab = source('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/tabs/plcTab.js');
  const cameraTab = source('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/tabs/cameraTab.js');
  const tcpTab = source('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/tabs/tcpTab.js');
  const aiTab = source('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/tabs/aiTab.js');
  const pilotConsole = source('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/tabs/runtimePreviewPilotConsole.js');
  const app = source('../../../../src/ClearVision.Product.Desktop/wwwroot/src/app.js');

  assert.match(systemTabs, /config\.expectedRevision\s*=\s*expectedRevision/);
  assert.match(systemTabs, /resetSettings\(expectedRevision\)/);
  assert.match(systemTabs, /requireAppConfigRevision\('系统设置'\)/);
  assert.match(plcTab, /expectedRevision\s*\n?\s*\}/);
  assert.match(plcTab, /requireAppConfigRevision\('PLC 配置'\)/);
  assert.doesNotMatch(plcTab, /settingsApi\.saveSettings\(/);
  assert.match(cameraTab, /requireAppConfigRevision\('相机配置'\)/);
  assert.match(cameraTab, /expectedRevision\s*\n?\s*\}/);
  assert.match(tcpTab, /saveTcpProfiles\(payload,\s*expectedRevision\)/);
  assert.match(aiTab, /payload\.expectedRevision\s*=\s*expectedRevision/);
  assert.match(pilotConsole, /payload\.expectedRevision\s*=\s*expectedRevision/);
  assert.match(app, /expectedRevision:\s*appConfigRevision/);
  assert.match(settingsView, /Number\(error\?\.status\s*\?\?\s*error\?\.statusCode\)\s*!==\s*409/);
  assert.match(settingsView, /Current tab fields\/state remain the user's draft/);
});

test('degraded AppConfig state blocks mutation before any API request', async () => {
  const view = createView();
  view.config.revision = 12;
  view.appConfigAuthorityHealthy = false;
  view.container = createContainer({
    fields: {
      ipAddress: createField('192.168.0.22'),
      port: createField('102'),
      cpuType: createField('S7-1200'),
      rack: createField('0'),
      slot: createField('1')
    }
  });
  const originalSave = settingsApi.savePlcSettings;
  let requests = 0;
  settingsApi.savePlcSettings = async () => {
    requests += 1;
    return { success: true };
  };

  let result;
  try {
    result = await view.savePlcSettings({ silent: true });
  } finally {
    settingsApi.savePlcSettings = originalSave;
  }

  assert.equal(result.unavailable, true);
  assert.equal(requests, 0);
  assert.equal(view.config.revision, 12);
});
