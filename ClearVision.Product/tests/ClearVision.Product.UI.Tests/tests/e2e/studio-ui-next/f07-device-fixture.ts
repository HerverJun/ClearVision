import { Buffer } from 'node:buffer';
import { expect, type Page } from '@playwright/test';
import {
  auditF02Request,
  fulfillF02Json,
  installF02BrowserStartup,
  type F02MethodAuditEntry
} from './f02-browser-fixture';

const fixtureSchema = 'f07-device-workbench.v1';
const onePixelPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=',
  'base64'
);

export interface F07DeviceAudit {
  readonly audit: F02MethodAuditEntry[];
  readonly state: {
    readonly tcpConnectCount: number;
    readonly tcpSendCount: number;
    readonly previewStartCount: number;
    readonly previewStopCount: number;
    readonly previewFrameCount: number;
  };
}

function fullSettingsPayload(): Record<string, unknown> {
  return {
    revision: 7,
    general: { softwareTitle: 'ClearVision Browser', theme: 'light', autoStart: false },
    storage: { imageSavePath: 'D:/VisionData', savePolicy: 'NgOnly', retentionDays: 30, minFreeSpaceGb: 5 },
    runtime: { autoRun: false, stopOnConsecutiveNg: 3, missingMaterialTimeoutSeconds: 120, applyProtectionRules: true },
    security: { passwordMinLength: 8, sessionTimeoutMinutes: 30, loginFailureLockoutCount: 5 },
    communication: {},
    tcpCommunication: {},
    features: {},
    cameras: [],
    activeCameraId: 'cam-fixture'
  };
}

function plcProfile(port: number, ipAddress = '127.0.0.1'): Record<string, unknown> {
  return {
    ipAddress,
    port,
    mappings: [{ name: 'Ready', address: 'M0', dataType: 'Bool', description: 'Fixture', canWrite: false }],
    cpuType: 'S7-1200',
    rack: 0,
    slot: 1
  };
}

function plcSettings(): Record<string, unknown> {
  return {
    activeProtocol: 'S7',
    heartbeatIntervalMs: 1000,
    s7: plcProfile(102),
    mc: plcProfile(5002),
    fins: plcProfile(9600)
  };
}

function tcpProfile(): Record<string, unknown> {
  return {
    id: 'tcp-fixture',
    name: 'Fixture Loopback',
    enabled: true,
    mode: 'Client',
    remoteHost: '127.0.0.1',
    remotePort: 9000,
    localHost: '127.0.0.1',
    localPort: 9001,
    encoding: 'UTF8',
    frameMode: 'Raw',
    fixedLength: 0,
    lineEnding: 'None',
    timeoutMs: 5000,
    keepAlive: false,
    reconnect: true,
    connectOnStartup: false,
    description: 'Deterministic TCP fixture'
  };
}

function cameraBinding(active = true): Record<string, unknown> {
  return {
    id: 'cam-fixture',
    displayName: 'Fixture Camera',
    deviceId: 'fixture-camera-1',
    serialNumber: 'fixture-camera-1',
    ipAddress: '192.168.0.10',
    manufacturer: 'Huaray',
    modelName: 'Fixture-1',
    interfaceType: 'GigE',
    isEnabled: true,
    isActive: active,
    exposureTimeUs: 5000,
    gainDb: 1,
    pixelFormat: 'Mono8',
    triggerMode: 'Software',
    hardwareTriggerSource: 'Line0',
    softwareTriggerSource: 'Manual',
    enterPhotoelectricDebounceMs: 200,
    enterPhotoelectricTimeoutMs: 30000,
    ignoreEnterTriggerWhileBusy: true,
    enterPhotoelectricDeviceId: '',
    serialPhotoelectricPortName: 'COM3',
    serialPhotoelectricBaudRate: 9600,
    serialPhotoelectricDebounceMs: 200,
    serialPhotoelectricTimeoutMs: 30000,
    ignoreSerialPhotoelectricTriggerWhileBusy: true,
    targetFrameRateFps: 30,
    connectionStatus: 'Connected'
  };
}

function discoveryDevice(manufacturer: string, cameraId: string): Record<string, unknown> {
  return {
    cameraId,
    name: `${manufacturer} Fixture`,
    serialNumber: `${cameraId}-serial`,
    manufacturer,
    model: `${manufacturer}-Mock-1`,
    userDefinedName: `${manufacturer} Fixture`,
    ipAddress: '192.168.0.10',
    connectionType: 'GigE',
    interfaceType: 'GigE',
    isConnected: true
  };
}

export async function installF07DeviceFixture(page: Page, role: 'Admin' | 'Engineer' | 'Operator'): Promise<F07DeviceAudit> {
  const audit: F02MethodAuditEntry[] = [];
  const counters = {
    tcpConnectCount: 0,
    tcpSendCount: 0,
    previewStartCount: 0,
    previewStopCount: 0,
    previewFrameCount: 0
  };
  let currentPlcSettings = plcSettings();
  let currentMappings = (currentPlcSettings.s7 as Record<string, unknown>).mappings;
  let currentProfiles = [tcpProfile()];
  let tcpConnected = false;
  let previewRunning = false;
  let activeCameraId = 'cam-fixture';
  let binding = cameraBinding(true);

  await installF02BrowserStartup(page);
  await page.route('**/health', async route => {
    audit.push(auditF02Request(route.request()));
    await fulfillF07Json(route, 200, { status: 'Healthy', port: 5177 });
  });
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    audit.push(auditF02Request(request));
    const method = request.method();

    if (url.pathname === '/api/auth/setup-status') {
      await fulfillF07Json(route, 200, { requiresInitialAdminSetup: false, usernameMinLength: 3, passwordMinLength: 6 });
      return;
    }
    if (url.pathname === '/api/auth/me') {
      await fulfillF07Json(route, 200, { userId: 'fixture-user', username: 'fixture-device', role });
      return;
    }
    if (url.pathname === '/api/settings') {
      await fulfillF07Json(route, 200, fullSettingsPayload());
      return;
    }

    if (url.pathname === '/api/plc/settings') {
      if (method === 'GET') {
        await fulfillF07Json(route, 200, { success: true, settings: currentPlcSettings });
        return;
      }
      if (role !== 'Admin') {
        await fulfillF07Json(route, 403, { error: 'AdminRequired' });
        return;
      }
      const body = JSON.parse(request.postData() ?? '{}') as Record<string, unknown>;
      if ((body.s7 as Record<string, unknown> | undefined)?.ipAddress === '10.0.0.99') {
        await fulfillF07Json(route, 200, {
          success: false,
          message: 'PLC 配置校验失败。',
          settings: currentPlcSettings,
          errors: [{ protocol: 'S7', profileId: null, section: 'settings', field: 'ipAddress', index: null, message: 'Fixture 拒绝该 PLC 地址。' }]
        });
        return;
      }
      currentPlcSettings = body;
      await fulfillF07Json(route, 200, { success: true, message: 'PLC 配置已保存。', settings: currentPlcSettings, errors: [] });
      return;
    }
    if (url.pathname === '/api/plc/mappings') {
      if (method === 'GET') {
        await fulfillF07Json(route, 200, currentMappings);
        return;
      }
      if (role !== 'Admin') {
        await fulfillF07Json(route, 403, { error: 'AdminRequired' });
        return;
      }
      currentMappings = JSON.parse(request.postData() ?? '[]');
      await fulfillF07Json(route, 200, { success: true, message: 'PLC 映射已保存。', mappings: currentMappings, errors: [] });
      return;
    }
    if (url.pathname === '/api/plc/test-connection') {
      if (role === 'Operator') {
        await fulfillF07Json(route, 403, { error: 'HardwareOperationPermissionRequired' });
        return;
      }
      await fulfillF07Json(route, 200, { success: false, message: 'Fixture PLC 未连接。', protocol: 'S7' });
      return;
    }

    if (url.pathname === '/api/tcp/profiles') {
      if (method === 'GET') {
        await fulfillF07Json(route, 200, { success: true, profiles: currentProfiles });
        return;
      }
      if (role !== 'Admin') {
        await fulfillF07Json(route, 403, { error: 'AdminRequired' });
        return;
      }
      currentProfiles = JSON.parse(request.postData() ?? '[]');
      await fulfillF07Json(route, 200, { success: true, message: 'TCP Profile 已保存。', profiles: currentProfiles, errors: [] });
      return;
    }
    const tcpMatch = url.pathname.match(/^\/api\/tcp\/profiles\/([^/]+)(?:\/(.*))?$/);
    if (tcpMatch) {
      const suffix = tcpMatch[2] ?? '';
      if (suffix === 'status') {
        await fulfillF07Json(route, 200, {
          success: true,
          status: {
            profileId: tcpMatch[1], mode: 'Client', isConnected: tcpConnected, isListening: false,
            localEndpoint: '127.0.0.1:9001', remoteEndpoint: '127.0.0.1:9000', connectedClients: 0,
            lastError: '', lastConnectedAtUtc: null, lastReceivedAtUtc: null, lastSentAtUtc: null
          }
        });
        return;
      }
      if (suffix === 'frames') {
        await fulfillF07Json(route, 200, { success: true, frames: tcpConnected ? [{ id: 'frame-1', profileId: tcpMatch[1], direction: 'Receive', timestampUtc: '2026-08-01T00:00:00Z', byteCount: 2, text: 'OK', hex: '4F 4B', remoteEndpoint: '127.0.0.1:9000' }] : [] });
        return;
      }
      if (role === 'Operator') {
        await fulfillF07Json(route, 403, { error: 'HardwareOperationPermissionRequired' });
        return;
      }
      if (suffix === 'connect') {
        tcpConnected = true;
        counters.tcpConnectCount += 1;
        await fulfillF07Json(route, 200, { success: true, message: 'TCP Client 已连接。', status: { profileId: tcpMatch[1], mode: 'Client', isConnected: true, isListening: false, localEndpoint: '127.0.0.1:9001', remoteEndpoint: '127.0.0.1:9000', connectedClients: 0, lastError: '', lastConnectedAtUtc: null, lastReceivedAtUtc: null, lastSentAtUtc: null }, errors: [] });
        return;
      }
      if (suffix === 'disconnect') {
        tcpConnected = false;
        await fulfillF07Json(route, 200, { success: true, message: 'TCP Client 已断开。', status: { profileId: tcpMatch[1], mode: 'Client', isConnected: false, isListening: false, localEndpoint: null, remoteEndpoint: null, connectedClients: 0, lastError: '', lastConnectedAtUtc: null, lastReceivedAtUtc: null, lastSentAtUtc: null }, errors: [] });
        return;
      }
      if (suffix === 'send') {
        counters.tcpSendCount += 1;
        await fulfillF07Json(route, 200, { success: true, message: 'TCP 报文已发送。', response: 'fixture-response', status: { profileId: tcpMatch[1], mode: 'Client', isConnected: true, isListening: false, localEndpoint: '127.0.0.1:9001', remoteEndpoint: '127.0.0.1:9000', connectedClients: 0, lastError: '', lastConnectedAtUtc: null, lastReceivedAtUtc: null, lastSentAtUtc: null }, errors: [] });
        return;
      }
      if (suffix === 'frames/clear') {
        await fulfillF07Json(route, 200, { success: true, message: 'TCP 收发日志已清空。' });
        return;
      }
    }

    if (url.pathname === '/api/cameras/discover') {
      await fulfillF07Json(route, 200, [discoveryDevice('Huaray', 'huaray-1'), discoveryDevice('Hikvision', 'hikvision-1')]);
      return;
    }
    if (url.pathname === '/api/cameras/discover/huaray') {
      await fulfillF07Json(route, 200, { devices: [discoveryDevice('Huaray', 'huaray-1')], diagnostics: { provider: 'huaray', fixture: true } });
      return;
    }
    if (url.pathname === '/api/cameras/discover/hikvision') {
      await fulfillF07Json(route, 200, [discoveryDevice('Hikvision', 'hikvision-1')]);
      return;
    }
    if (url.pathname === '/api/cameras/bindings') {
      if (method === 'GET') {
        await fulfillF07Json(route, 200, [{ ...binding, isActive: activeCameraId === 'cam-fixture' }]);
        return;
      }
      if (role === 'Operator') {
        await fulfillF07Json(route, 403, { error: 'HardwareOperationPermissionRequired' });
        return;
      }
      const body = JSON.parse(request.postData() ?? '{}') as { bindings?: Record<string, unknown>[]; activeCameraId?: string };
      const nextBinding = body.bindings?.[0];
      if (previewRunning && nextBinding?.exposureTimeUs !== binding.exposureTimeUs) {
        await fulfillF07Json(route, 409, { error: 'Camera stream is active; stop preview before changing runtime settings.' });
        return;
      }
      binding = nextBinding ?? binding;
      activeCameraId = body.activeCameraId ?? activeCameraId;
      await fulfillF07Json(route, 200, { Message: '相机配置已保存。' });
      return;
    }
    if (url.pathname === '/api/trigger-input/diagnostics') {
      await fulfillF07Json(route, 200, { isAvailable: true, listenerType: 'Fixture', pendingWaiterCount: 0, attachedWindowHandle: null, lastDeviceId: null, lastSignalUtc: null, lastError: null });
      return;
    }
    if (url.pathname === '/api/trigger-input/serial-photoelectric-ports') {
      await fulfillF07Json(route, 200, [{ portName: 'COM3', displayName: 'Fixture Serial', isRecommended: true }]);
      return;
    }
    if (url.pathname === '/api/trigger-input/test-serial-photoelectric') {
      await fulfillF07Json(route, 200, { Message: '串口光电测试成功', Source: 'Fixture', PortName: 'COM3', TimestampUtc: '2026-08-01T00:00:00Z' });
      return;
    }
    if (url.pathname === '/api/trigger-input/learn-enter-device') {
      await fulfillF07Json(route, 200, { deviceId: 'fixture-enter', timestampUtc: '2026-08-01T00:00:00Z' });
      return;
    }
    if (url.pathname === '/api/cameras/soft-trigger-capture') {
      if (role === 'Operator') {
        await fulfillF07Json(route, 403, { error: 'HardwareOperationPermissionRequired' });
        return;
      }
      await route.fulfill({
        status: 200,
        contentType: 'image/png',
        headers: { 'X-Camera-Id': 'cam-fixture', 'X-Trigger-Mode': 'Software', 'X-Trigger-Source': 'Manual', 'X-Image-Width': '1', 'X-Image-Height': '1', ...fixtureHeaders('POST /api/cameras/soft-trigger-capture') },
        body: onePixelPng
      });
      return;
    }
    if (url.pathname === '/api/cameras/continuous-preview/start') {
      if (role === 'Operator') {
        await fulfillF07Json(route, 403, { error: 'HardwareOperationPermissionRequired' });
        return;
      }
      previewRunning = true;
      counters.previewStartCount += 1;
      await fulfillF07Json(route, 200, { sessionId: 'fixture-session', cameraBindingId: 'cam-fixture', triggerMode: 'Software', targetFrameRateFps: 10 });
      return;
    }
    const frameMatch = url.pathname.match(/^\/api\/cameras\/continuous-preview\/frame\/([^/]+)$/);
    if (frameMatch) {
      if (!previewRunning) {
        await fulfillF07Json(route, 404, { error: 'Preview session stopped.' });
        return;
      }
      counters.previewFrameCount += 1;
      await route.fulfill({
        status: 200,
        contentType: 'image/png',
        headers: { 'X-Image-Width': '1', 'X-Image-Height': '1', 'X-Camera-Id': 'cam-fixture', 'X-Frame-Sequence': String(counters.previewFrameCount), ...fixtureHeaders(`GET ${url.pathname}`) },
        body: onePixelPng
      });
      return;
    }
    if (url.pathname === '/api/cameras/continuous-preview/stop') {
      previewRunning = false;
      counters.previewStopCount += 1;
      await fulfillF07Json(route, 200, { Message: 'Continuous preview session stopped.' });
      return;
    }

    await fulfillF07Json(route, 404, { error: 'NotFound' });
  });

  return { audit, state: counters };
}

function fixtureHeaders(endpoint: string): Record<string, string> {
  return {
    'x-clearvision-fixture-schema': fixtureSchema,
    'x-clearvision-fixture-endpoint': endpoint,
    'x-clearvision-fixture-source-sha': '235dccfd246d5b204463f103d1652ef90a11745d',
    'x-clearvision-data-source': 'BROWSER_FIXTURE',
    'x-clearvision-auth-source': 'HARNESS_SEEDED_SESSION'
  };
}

async function fulfillF07Json(route: Parameters<typeof fulfillF02Json>[0], status: number, body: unknown): Promise<void> {
  await fulfillF02Json(route, status, body, fixtureSchema);
}

export function expectNoFixtureErrors(audit: F07DeviceAudit): void {
  expect(audit.audit.some(entry => entry.path.includes('/api/settings/import'))).toBe(false);
  expect(audit.audit.some(entry => entry.path.includes('/api/settings/export'))).toBe(false);
}
