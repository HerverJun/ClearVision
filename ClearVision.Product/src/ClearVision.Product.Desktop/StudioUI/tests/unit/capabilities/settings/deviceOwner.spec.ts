import { describe, expect, it, vi } from 'vitest';
import type { ProductRuntime } from '@/app/productRuntime';
import { ApiConflictError, type ApiTransport } from '@/platform/api';
import {
  createSettingsOwner,
  type CameraBindingV1,
  type SettingsOwner
} from '@/capabilities/settings';

function runtime(api: ApiTransport): Pick<ProductRuntime, 'api'> {
  return { api };
}

function binding(): CameraBindingV1 {
  return {
    id: 'cam-1', displayName: 'Fixture Camera', deviceId: 'fixture-serial', serialNumber: 'fixture-serial',
    ipAddress: '192.168.0.10', manufacturer: 'Huaray', modelName: 'Fixture-1', interfaceType: 'GigE',
    isEnabled: true, isActive: true, exposureTimeUs: 5000, gainDb: 1, pixelFormat: 'Mono8',
    triggerMode: 'Software', hardwareTriggerSource: 'Line0', softwareTriggerSource: 'Manual',
    enterPhotoelectricDebounceMs: 200, enterPhotoelectricTimeoutMs: 30000, ignoreEnterTriggerWhileBusy: true,
    enterPhotoelectricDeviceId: '', serialPhotoelectricPortName: '', serialPhotoelectricBaudRate: 9600,
    serialPhotoelectricDebounceMs: 200, serialPhotoelectricTimeoutMs: 30000,
    ignoreSerialPhotoelectricTriggerWhileBusy: true, targetFrameRateFps: 30, connectionStatus: 'Connected'
  };
}

function apiBase(overrides: Partial<ApiTransport> = {}): ApiTransport {
  return {
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T = unknown>(): Promise<T | undefined> {
      return undefined;
    },
    ...overrides
  } as ApiTransport;
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

describe('F07 G6 Settings owner preview lifecycle', () => {
  it('aborts the frame waiter and sends the authorized stop endpoint on explicit stop', async () => {
    let frameSignal: AbortSignal | undefined;
    const post = vi.fn(async (path: string) => {
      if (path === 'cameras/continuous-preview/start') {
        return { sessionId: 'session-1', cameraBindingId: 'cam-1', triggerMode: 'Software', targetFrameRateFps: 30 };
      }
      return { Message: 'stopped' };
    }) as NonNullable<ApiTransport['post']>;
    const getBlob = vi.fn((_path: string, options?: { readonly signal?: AbortSignal }) => new Promise<never>((_resolve, reject) => {
      frameSignal = options?.signal;
      options?.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')), { once: true });
    })) as NonNullable<ApiTransport['getBlob']>;
    const owner = createSettingsOwner({
      runtime: runtime(apiBase({ post, getBlob })),
      role: 'Engineer'
    });

    const started = await owner.startCameraPreview('cam-1');
    await vi.waitFor(() => expect(getBlob).toHaveBeenCalled(), { timeout: 1000 });
    const stopped = await owner.stopCameraPreview('test-stop');

    expect(started).toMatchObject({ status: 'completed', value: { success: true } });
    expect(frameSignal?.aborted).toBe(true);
    expect(stopped).toMatchObject({ status: 'completed' });
    expect(post).toHaveBeenCalledWith('cameras/continuous-preview/stop', { sessionId: 'session-1' }, expect.anything());
    expect(owner.projection.device.preview.phase).toBe('idle');
    expect(owner.diagnostics().preview).toMatchObject({
      controllerCount: 0,
      sessionCount: 0,
      frameLoopCount: 0,
      blobUrlCount: 0
    });
    expect(owner.diagnostics().activeAbortControllerCount).toBe(0);
    owner.dispose();
  });

  it('keeps camera binding projection unchanged when the backend returns 409', async () => {
    const put = vi.fn(async () => {
      throw new ApiConflictError({
        url: 'http://localhost:5000/api/cameras/bindings',
        status: 409,
        statusText: 'Conflict',
        payload: { code: 'camera_stream_active', message: 'active stream' },
        responseBody: '{"code":"camera_stream_active"}'
      });
    }) as NonNullable<ApiTransport['put']>;
    const owner: SettingsOwner = createSettingsOwner({ runtime: runtime(apiBase({ put })), role: 'Admin' });
    const next = { ...binding(), exposureTimeUs: 7000 };

    const result = await owner.saveCameraBindings([next], 'cam-1');

    expect(result.status).toBe('failed');
    expect(owner.projection.device.cameraBindings).toEqual([]);
    expect(put).toHaveBeenCalledWith('cameras/bindings', expect.objectContaining({ activeCameraId: 'cam-1' }), expect.anything());
    owner.dispose();
  });

  it('cancels Camera work without staling parallel PLC and TCP operations', async () => {
    const cameraStart = deferred<unknown>();
    const plcOperation = deferred<unknown>();
    const tcpOperation = deferred<unknown>();
    let cameraSignal: AbortSignal | undefined;
    const post = vi.fn(async (
      path: string,
      _body?: unknown,
      options?: { readonly signal?: AbortSignal }
    ) => {
      if (path === 'cameras/continuous-preview/start') {
        cameraSignal = options?.signal;
        return cameraStart.promise;
      }
      if (path === 'plc/test-connection') return plcOperation.promise;
      if (path === 'tcp/profiles/tcp-1/connect') return tcpOperation.promise;
      if (path === 'cameras/continuous-preview/stop') return { Message: 'stopped' };
      throw new Error(`unexpected path: ${path}`);
    }) as NonNullable<ApiTransport['post']>;
    const owner = createSettingsOwner({ runtime: runtime(apiBase({ post })), role: 'Admin' });

    const camera = owner.startCameraPreview('cam-1');
    const plc = owner.testPlcConnection({ protocol: 'S7', ipAddress: '127.0.0.1', port: 102 });
    const tcp = owner.connectTcp('tcp-1');
    await vi.waitFor(() => expect(post).toHaveBeenCalledTimes(3));

    const stop = owner.stopCameraPreview('camera-cancel-test');
    expect(cameraSignal?.aborted).toBe(true);

    plcOperation.resolve({ success: true, message: 'PLC ready', protocol: 'S7' });
    tcpOperation.resolve({
      success: true,
      message: 'TCP connected',
      response: '',
      status: {
        profileId: 'tcp-1', mode: 'Client', isConnected: true, isListening: false,
        localEndpoint: '127.0.0.1:9001', remoteEndpoint: '127.0.0.1:9000', connectedClients: 0,
        lastError: '', lastConnectedAtUtc: null, lastReceivedAtUtc: null, lastSentAtUtc: null
      },
      errors: []
    });
    cameraStart.resolve({
      sessionId: 'session-cancelled', cameraBindingId: 'cam-1', triggerMode: 'Software', targetFrameRateFps: 30
    });

    expect((await plc).status).toBe('completed');
    expect((await tcp).status).toBe('completed');
    expect((await camera).status).toBe('cancelled');
    expect((await stop).status).toBe('completed');
    expect(owner.diagnostics().write).toMatchObject({
      activeSectionCount: 0,
      activeAbortControllerCount: 0,
      queuedTaskCount: 0
    });
    owner.dispose();
  });
});
