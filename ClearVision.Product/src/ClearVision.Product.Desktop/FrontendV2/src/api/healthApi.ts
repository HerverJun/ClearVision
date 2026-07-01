import type { LegacyHttpClient } from '@/adapters/legacyModules';

export interface HealthResponse {
  readonly status?: string;
  readonly port?: number;
  readonly [key: string]: unknown;
}

export function createHealthApi(httpClient: LegacyHttpClient) {
  return {
    getHealth(signal?: AbortSignal): Promise<HealthResponse> {
      const options = signal ? { signal } : undefined;
      return httpClient.getRoot<HealthResponse>('/health', null, options);
    }
  };
}
