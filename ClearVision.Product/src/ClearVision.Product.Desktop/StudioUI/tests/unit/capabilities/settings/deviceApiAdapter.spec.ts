import { describe, expect, it, vi } from 'vitest';
import type { ApiBlobResponse, ApiTransport } from '@/platform/api';
import {
  createSettingsDeviceApiAdapter,
  type PlcTestConnectionRequestV1,
  type TcpSendRequestV1
} from '@/capabilities/settings';
import type { PlcSettingsV1, TcpProfileV1 } from '@/capabilities/settings';

function blobResponse(): ApiBlobResponse {
  return {
    blob: new Blob(['fixture'], { type: 'image/png' }),
    contentType: 'image/png',
    contentLength: 7,
    etag: null,
    sha256: null,
    headers: new Headers({
      'X-Camera-Id': 'cam-1',
      'X-Trigger-Mode': 'Software',
      'X-Trigger-Source': 'Manual',
      'X-Image-Width': '640',
      'X-Image-Height': '480'
    })
  };
}

function plcSettings(): PlcSettingsV1 {
  const profile = (port: number) => ({
    ipAddress: '127.0.0.1', port, mappings: [], cpuType: 'S7-1200', rack: 0, slot: 1
  });
  return { activeProtocol: 'S7', heartbeatIntervalMs: 1000, s7: profile(102), mc: profile(5002), fins: profile(9600) };
}

function tcpProfile(): TcpProfileV1 {
  return {
    id: 'tcp-client', name: 'Loopback', enabled: true, mode: 'Client', remoteHost: '127.0.0.1', remotePort: 9000,
    localHost: '127.0.0.1', localPort: 0, encoding: 'UTF8', frameMode: 'Raw', fixedLength: 0, lineEnding: 'None',
    timeoutMs: 5000, keepAlive: false, reconnect: true, connectOnStartup: false, description: ''
  };
}

describe('F07 G5/G6 dedicated device API adapter', () => {
  it('keeps PLC/TCP persistence and runtime operations on dedicated endpoint paths', async () => {
    const get = vi.fn(async (path: string) => {
      if (path === 'plc/settings') return { success: true, settings: plcSettings() };
      if (path === 'tcp/profiles') return { success: true, profiles: [tcpProfile()] };
      if (path.includes('/status')) return { success: true, status: null };
      if (path.includes('/frames')) return { success: true, frames: [] };
      throw new Error(`unexpected GET ${path}`);
    }) as NonNullable<ApiTransport['get']>;
    const put = vi.fn(async (path: string, body: unknown) => {
      if (path === 'plc/settings') return { success: true, settings: plcSettings() };
      if (path === 'tcp/profiles') return { success: true, profiles: body };
      throw new Error(`unexpected PUT ${path}`);
    }) as NonNullable<ApiTransport['put']>;
    const post = vi.fn(async (path: string, body: unknown) => {
      if (path === 'plc/test-connection') return { success: true, message: 'ok', protocol: 'S7' };
      if (path.endsWith('/connect') || path.endsWith('/disconnect') || path.endsWith('/server/start') || path.endsWith('/server/stop')) {
        return { success: true, message: 'ok', status: null, response: '', errors: [] };
      }
      if (path.endsWith('/send')) return { success: true, message: 'sent', response: 'OK', status: null, errors: [] };
      if (path.endsWith('/frames/clear')) return { success: true, message: 'cleared' };
      throw new Error(`unexpected POST ${path} ${JSON.stringify(body)}`);
    }) as NonNullable<ApiTransport['post']>;
    const api = { apiBaseUrl: 'http://localhost:5000/api', get, put, post } as ApiTransport;
    const adapter = createSettingsDeviceApiAdapter(api);
    const testRequest: PlcTestConnectionRequestV1 = { protocol: 'S7', ipAddress: '127.0.0.1', port: 102, cpuType: 'S7-1200', rack: 0, slot: 1 };
    const sendRequest: TcpSendRequestV1 = { payload: '4F 4B', isHex: true, waitResponse: true, responseTimeoutMs: 1000 };

    await adapter.readPlcSettings();
    await adapter.writePlcSettings(plcSettings());
    await adapter.testPlcConnection(testRequest);
    await adapter.readTcpProfiles();
    await adapter.writeTcpProfiles([tcpProfile()]);
    await adapter.connectTcp('tcp-client');
    await adapter.sendTcp('tcp-client', sendRequest);

    expect(get).toHaveBeenCalledWith('plc/settings', expect.anything());
    expect(put).toHaveBeenCalledWith('plc/settings', expect.objectContaining({ activeProtocol: 'S7', s7: expect.any(Object) }), expect.anything());
    expect(post).toHaveBeenCalledWith('plc/test-connection', testRequest, expect.anything());
    expect(put).toHaveBeenCalledWith('tcp/profiles', [tcpProfile()], expect.anything());
    expect(post).toHaveBeenCalledWith('tcp/profiles/tcp-client/connect', {}, expect.anything());
    expect(post).toHaveBeenCalledWith('tcp/profiles/tcp-client/send', expect.objectContaining({ isHex: true, payload: '4F 4B' }), expect.anything());
  });

  it('uses provider-specific discovery and blob preview endpoints', async () => {
    const get = vi.fn(async (path: string) => {
      if (path === 'cameras/discover/huaray') return { devices: [], diagnostics: { provider: 'huaray' } };
      throw new Error(`unexpected GET ${path}`);
    }) as NonNullable<ApiTransport['get']>;
    const post = vi.fn(async (path: string) => {
      if (path === 'cameras/continuous-preview/start') {
        return { sessionId: 'session-1', cameraBindingId: 'cam-1', triggerMode: 'Software', targetFrameRateFps: 30 };
      }
      return { Message: 'stopped' };
    }) as NonNullable<ApiTransport['post']>;
    const postBlob = vi.fn(async () => blobResponse()) as NonNullable<ApiTransport['postBlob']>;
    const getBlob = vi.fn(async () => blobResponse()) as NonNullable<ApiTransport['getBlob']>;
    const api = { apiBaseUrl: 'http://localhost:5000/api', get, post, postBlob, getBlob } as ApiTransport;
    const adapter = createSettingsDeviceApiAdapter(api);

    const discovery = await adapter.discoverCameras('huaray');
    const capture = await adapter.softTriggerCapture('cam-1');
    const session = await adapter.startContinuousPreview('cam-1');
    await adapter.getContinuousPreviewFrame(session.sessionId);
    await adapter.stopContinuousPreview(session.sessionId);

    expect(discovery.diagnostics).toMatchObject({ provider: 'huaray' });
    expect(capture).toMatchObject({ cameraBindingId: 'cam-1', width: 640, height: 480 });
    expect(postBlob).toHaveBeenCalledWith('cameras/soft-trigger-capture', { cameraBindingId: 'cam-1' }, expect.anything());
    expect(post).toHaveBeenCalledWith('cameras/continuous-preview/start', { cameraBindingId: 'cam-1' }, expect.anything());
    expect(getBlob).toHaveBeenCalledWith(expect.stringMatching(/^cameras\/continuous-preview\/frame\/session-1\?_=/), expect.anything());
    expect(post).toHaveBeenCalledWith('cameras/continuous-preview/stop', { sessionId: 'session-1' }, expect.anything());
  });
});
