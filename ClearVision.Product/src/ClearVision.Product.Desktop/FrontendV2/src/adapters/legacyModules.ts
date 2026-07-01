export interface LegacyHttpClient {
  readonly baseUrl?: string;
  getRoot<T = unknown>(
    url: string,
    params?: Record<string, string> | null,
    options?: { readonly signal?: AbortSignal }
  ): Promise<T>;
}

export interface LegacyWebMessageBridge {
  on(type: string, handler: (payload: unknown) => void): () => void;
  sendMessage(type: string, data?: unknown, expectResponse?: boolean): Promise<unknown>;
  clearPendingRequests?(error?: Error): void;
}

export interface LegacyEventBus {
  on(eventName: string, handler: (payload: unknown) => void): () => void;
  emit(eventName: string, payload?: unknown): void;
}

export interface LegacyServiceRegistry {
  register(key: string, service: unknown): unknown;
  unregister(key: string, expectedService?: unknown): boolean;
}

export interface LegacyFrontendServices {
  readonly httpClient: LegacyHttpClient;
  readonly webMessageBridge: LegacyWebMessageBridge;
  readonly eventBus: LegacyEventBus;
  readonly serviceRegistry: LegacyServiceRegistry;
}

type DefaultModule<T> = {
  readonly default: T;
};

const legacyModulePaths = {
  httpClient: '/src/core/messaging/httpClient.js',
  webMessageBridge: '/src/core/messaging/webMessageBridge.js',
  eventBus: '/src/core/app/eventBus.js',
  serviceRegistry: '/src/core/app/serviceRegistry.js'
} as const;

export async function loadLegacyFrontendServices(): Promise<LegacyFrontendServices> {
  const [httpClientModule, webMessageBridgeModule, eventBusModule, serviceRegistryModule] = await Promise.all([
    import(/* @vite-ignore */ legacyModulePaths.httpClient) as Promise<DefaultModule<LegacyHttpClient>>,
    import(/* @vite-ignore */ legacyModulePaths.webMessageBridge) as Promise<DefaultModule<LegacyWebMessageBridge>>,
    import(/* @vite-ignore */ legacyModulePaths.eventBus) as Promise<DefaultModule<LegacyEventBus>>,
    import(/* @vite-ignore */ legacyModulePaths.serviceRegistry) as Promise<DefaultModule<LegacyServiceRegistry>>
  ]);

  return {
    httpClient: httpClientModule.default,
    webMessageBridge: webMessageBridgeModule.default,
    eventBus: eventBusModule.default,
    serviceRegistry: serviceRegistryModule.default
  };
}
