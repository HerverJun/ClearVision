import {
  ApiAbortError,
  ApiDecodeError,
  ApiHttpError,
  ApiNetworkError,
  type ApiTransport
} from '@/platform/api';

export type DiagnosticProbeState = 'pending' | 'ok' | 'error' | 'aborted';

export interface DiagnosticProbeResult {
  readonly state: DiagnosticProbeState;
  readonly summary: string;
}

export interface StudioRuntimeProbeResult {
  readonly health: DiagnosticProbeResult;
  readonly setupStatus: DiagnosticProbeResult;
}

export interface StudioRuntimeDiagnosticsProbe {
  read(): Promise<StudioRuntimeProbeResult>;
  dispose(): void;
}

export class StudioRuntimeDiagnosticsDisposedError extends Error {
  constructor() {
    super('The StudioUI runtime diagnostics probe has been disposed.');
    this.name = 'StudioRuntimeDiagnosticsDisposedError';
  }
}

function summarizePayload(payload: unknown): string {
  if (payload === undefined) {
    return 'Empty successful response';
  }

  try {
    const serialized = JSON.stringify(payload);
    if (!serialized) {
      return 'Successful response';
    }

    return serialized.length > 240
      ? `${serialized.slice(0, 237)}...`
      : serialized;
  } catch {
    return 'Successful response';
  }
}

function summarizeProbeError(error: unknown, signal: AbortSignal): DiagnosticProbeResult {
  if (signal.aborted || error instanceof ApiAbortError) {
    return Object.freeze({ state: 'aborted', summary: 'Request aborted' });
  }

  if (error instanceof ApiHttpError) {
    return Object.freeze({
      state: 'error',
      summary: `HTTP ${error.status}${error.statusText ? ` ${error.statusText}` : ''}`
    });
  }

  if (error instanceof ApiDecodeError) {
    return Object.freeze({ state: 'error', summary: 'Response was not valid JSON' });
  }

  if (error instanceof ApiNetworkError) {
    return Object.freeze({ state: 'error', summary: 'Network request failed' });
  }

  return Object.freeze({ state: 'error', summary: 'Unexpected diagnostics failure' });
}

async function readEndpoint(
  api: ApiTransport,
  path: string,
  signal: AbortSignal
): Promise<DiagnosticProbeResult> {
  try {
    const payload = await api.get(path, { signal });
    return Object.freeze({ state: 'ok', summary: summarizePayload(payload) });
  } catch (error) {
    return summarizeProbeError(error, signal);
  }
}

export function createStudioRuntimeDiagnosticsProbe(
  api: ApiTransport
): StudioRuntimeDiagnosticsProbe {
  let activeController: AbortController | undefined;
  let disposed = false;

  return Object.freeze({
    async read(): Promise<StudioRuntimeProbeResult> {
      if (disposed) {
        throw new StudioRuntimeDiagnosticsDisposedError();
      }

      activeController?.abort('superseded');
      const controller = new AbortController();
      activeController = controller;

      const [health, setupStatus] = await Promise.all([
        readEndpoint(api, '/health', controller.signal),
        readEndpoint(api, 'auth/setup-status', controller.signal)
      ]);

      if (activeController === controller) {
        activeController = undefined;
      }

      return Object.freeze({ health, setupStatus });
    },
    dispose(): void {
      if (disposed) {
        return;
      }

      disposed = true;
      activeController?.abort('disposed');
      activeController = undefined;
    }
  });
}
