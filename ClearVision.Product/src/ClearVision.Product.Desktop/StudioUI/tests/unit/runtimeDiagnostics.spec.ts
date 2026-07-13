import { describe, expect, it } from 'vitest';
import {
  ApiAbortError,
  type ApiGetOptions,
  type ApiTransport
} from '@/platform/api';
import {
  createStudioRuntimeDiagnosticsProbe,
  StudioRuntimeDiagnosticsDisposedError
} from '@/platform/diagnostics/runtimeDiagnostics';

describe('Studio runtime diagnostics probe', () => {
  it('reads only the public health and setup-status endpoints', async () => {
    const requestedPaths: string[] = [];
    const api: ApiTransport = Object.freeze({
      apiBaseUrl: 'http://localhost:5000/api',
      async get<T>(path: string): Promise<T | undefined> {
        requestedPaths.push(path);
        return (path === '/health'
          ? { status: 'Healthy', port: 5000 }
          : { requiresInitialAdminSetup: false }) as T;
      }
    });
    const probe = createStudioRuntimeDiagnosticsProbe(api);

    const result = await probe.read();

    expect(requestedPaths).toEqual(['/health', 'auth/setup-status']);
    expect(result.health).toEqual({
      state: 'ok',
      summary: '{"status":"Healthy","port":5000}'
    });
    expect(result.setupStatus.state).toBe('ok');
  });

  it('aborts active reads and rejects reuse after dispose', async () => {
    const signals: AbortSignal[] = [];
    const api: ApiTransport = Object.freeze({
      apiBaseUrl: 'http://localhost:5000/api',
      get<T>(path: string, options: ApiGetOptions = {}): Promise<T | undefined> {
        const signal = options.signal;
        if (!signal) {
          throw new Error('Expected an AbortSignal.');
        }

        signals.push(signal);
        return new Promise<T | undefined>((_, reject) => {
          signal.addEventListener('abort', () => {
            reject(new ApiAbortError(path, signal.reason));
          }, { once: true });
        });
      }
    });
    const probe = createStudioRuntimeDiagnosticsProbe(api);
    const pendingRead = probe.read();

    probe.dispose();

    const result = await pendingRead;
    expect(signals).toHaveLength(2);
    expect(signals.every(signal => signal.aborted)).toBe(true);
    expect(result.health.state).toBe('aborted');
    expect(result.setupStatus.state).toBe('aborted');
    await expect(probe.read()).rejects.toBeInstanceOf(
      StudioRuntimeDiagnosticsDisposedError
    );
  });
});
