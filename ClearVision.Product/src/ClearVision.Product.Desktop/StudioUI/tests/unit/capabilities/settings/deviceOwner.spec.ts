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
    const owner: SettingsOwner = createSettingsOwner({ runtime: runtime(apiBase({ put })), role: 'Engineer' });
    const next = { ...binding(), exposureTimeUs: 7000 };

    const result = await owner.saveCameraBindings([next], 'cam-1');

    expect(result.status).toBe('failed');
    expect(owner.projection.device.cameraBindings).toEqual([]);
    expect(put).toHaveBeenCalledWith('cameras/bindings', expect.objectContaining({ activeCameraId: 'cam-1' }), expect.anything());
    owner.dispose();
  });
});
