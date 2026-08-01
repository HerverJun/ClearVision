import { describe, expect, it } from 'vitest';
import {
  decodeCameraBindingsResponse,
  decodeCameraDiscoveryResponse,
  decodePlcMappingsResponse,
  decodePlcSettingsResponse,
  decodeTcpFramesResponse,
  decodeTcpProfilesResponse,
  decodeTriggerDiagnostics
} from '@/capabilities/settings';

function plcProfile(port: number, mappings: readonly unknown[] = []) {
  return {
    ipAddress: '127.0.0.1',
    port,
    mappings,
    cpuType: 'S7-1200',
    rack: 0,
    slot: 1
  };
}

function cameraBinding(overrides: Record<string, unknown> = {}) {
  return {
    id: 'cam-1',
    displayName: 'Fixture Camera',
    deviceId: 'fixture-serial',
    serialNumber: 'fixture-serial',
    ipAddress: '192.168.0.10',
    manufacturer: 'Huaray',
    modelName: 'Fixture-1',
    interfaceType: 'GigE',
    isEnabled: true,
    isActive: true,
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
    serialPhotoelectricPortName: '',
    serialPhotoelectricBaudRate: 9600,
    serialPhotoelectricDebounceMs: 200,
    serialPhotoelectricTimeoutMs: 30000,
    ignoreSerialPhotoelectricTriggerWhileBusy: true,
    targetFrameRateFps: 30,
    connectionStatus: 'Connected',
    ...overrides
  };
}

describe('F07 G5/G6 device decoders', () => {
  it('decodes isolated PLC protocol profiles and mapping validation issues', () => {
    const result = decodePlcSettingsResponse({
      success: true,
      message: 'loaded',
      settings: {
        activeProtocol: 'SiemensS7',
        heartbeatIntervalMs: 1000,
        s7: plcProfile(102, [{ name: 'Ready', address: 'DB1.DBX0.0', dataType: 'Bool', canWrite: false }]),
        mc: { ipAddress: '127.0.0.1', port: 5002, mappings: [] },
        fins: { ipAddress: '127.0.0.1', port: 9600, mappings: [] }
      },
      errors: [{ protocol: 'S7', profileId: null, section: 'mapping', field: 'address', index: 0, message: 'invalid' }]
    });

    expect(result.settings?.activeProtocol).toBe('S7');
    expect(result.settings?.s7.mappings[0]?.address).toBe('DB1.DBX0.0');
    expect(result.errors[0]).toMatchObject({ protocol: 'S7', index: 0, message: 'invalid' });
    expect(decodePlcMappingsResponse([{ name: 'Run', address: 'M0', dataType: 'Bool', canWrite: true }]).mappings)
      .toHaveLength(1);
  });

  it('keeps TCP profiles, status and bounded frames typed', () => {
    const profiles = decodeTcpProfilesResponse({
      success: true,
      profiles: [{
        id: 'tcp-client', name: 'Loopback', enabled: true, mode: 'Client',
        remoteHost: '127.0.0.1', remotePort: 9000, localHost: '127.0.0.1', localPort: 0,
        encoding: 'UTF8', frameMode: 'Line', fixedLength: 0, lineEnding: 'LF', timeoutMs: 5000,
        keepAlive: false, reconnect: true, connectOnStartup: false, description: ''
      }]
    });
    const frames = decodeTcpFramesResponse({
      success: true,
      frames: [{
        id: 'frame-1', profileId: 'tcp-client', direction: 'Receive', timestampUtc: '2026-08-01T00:00:00Z',
        byteCount: 3, text: 'OK', hex: '4F 4B', remoteEndpoint: '127.0.0.1:9000'
      }]
    });

    expect(profiles.profiles[0]).toMatchObject({ id: 'tcp-client', mode: 'Client', frameMode: 'Line' });
    expect(frames.frames[0]).toMatchObject({ direction: 'Receive', byteCount: 3 });
  });

  it('accepts array/object camera discovery and projects additive IsActive', () => {
    const arrayDiscovery = decodeCameraDiscoveryResponse([{
      cameraId: 'cam-1', name: 'Fixture', serialNumber: 'fixture-serial', manufacturer: 'Huaray',
      model: 'Fixture-1', userDefinedName: 'Fixture Camera', ipAddress: '192.168.0.10',
      connectionType: 'GigE', interfaceType: 'GigE', isConnected: true
    }]);
    const objectDiscovery = decodeCameraDiscoveryResponse({
      devices: arrayDiscovery.devices,
      diagnostics: { provider: 'huaray', sdkAvailable: true }
    });
    const bindings = decodeCameraBindingsResponse([cameraBinding()]);

    expect(arrayDiscovery.devices[0]?.manufacturer).toBe('Huaray');
    expect(objectDiscovery.diagnostics).toMatchObject({ provider: 'huaray', sdkAvailable: true });
    expect(bindings.activeCameraId).toBe('cam-1');
    expect(bindings.bindings[0]?.isActive).toBe(true);
  });

  it('decodes trigger diagnostics without exposing unknown fields', () => {
    const diagnostics = decodeTriggerDiagnostics({
      isAvailable: true,
      listenerType: 'WindowMessage',
      pendingWaiterCount: 1,
      attachedWindowHandle: '0x10',
      lastDeviceId: 'enter-1',
      lastSignalUtc: '2026-08-01T00:00:00Z',
      lastError: null,
      token: 'must-not-project'
    });

    expect(diagnostics).toMatchObject({ isAvailable: true, listenerType: 'WindowMessage', pendingWaiterCount: 1 });
    expect(diagnostics).not.toHaveProperty('token');
  });
});
