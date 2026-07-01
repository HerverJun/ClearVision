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

export interface HostedFlowCanvasViewState {
  readonly selectedNode: string | null;
  readonly selectedConnection: string | null;
  readonly scale: number;
  readonly offset: {
    readonly x: number;
    readonly y: number;
  };
  readonly nodeCount: number;
  readonly connectionCount: number;
}

export interface LegacyFlowCanvasSnapshot {
  readonly flowRevision: number;
  readonly selectionRevision: number;
  readonly selectedNodeId: string | null;
  readonly flow: unknown;
  readonly selectedNode: unknown;
}

export interface LegacyFlowCanvasParameterPatchResult {
  readonly updated: boolean;
  readonly reason: 'updated' | 'no_change' | 'node_not_found' | 'parameter_not_found';
  readonly missingParameters: readonly string[];
}

export interface LegacyFlowCanvasAdapter {
  resize(): unknown;
  render(): unknown;
  dispose(): void;
  getViewState(): HostedFlowCanvasViewState;
  getSnapshot(): LegacyFlowCanvasSnapshot;
  replaceFlow(flow: unknown): unknown;
  selectNode(nodeId: string | null): boolean;
  patchNodeParameters(
    nodeId: string,
    parameterPatch: Readonly<Record<string, unknown>>
  ): LegacyFlowCanvasParameterPatchResult;
  subscribeStructure(listener: (payload: unknown) => void): () => void;
  subscribeSelection(listener: (payload: unknown) => void): () => void;
}

export type HostedFlowCanvasAdapter = LegacyFlowCanvasAdapter;

export interface LegacyFlowCanvasAdapterModule {
  createHostedFlowCanvasAdapter(
    canvasId: string,
    options?: { readonly eventBus?: LegacyEventBus }
  ): HostedFlowCanvasAdapter;
}

export interface LegacyFrontendServices {
  readonly httpClient: LegacyHttpClient;
  readonly webMessageBridge: LegacyWebMessageBridge;
  readonly eventBus: LegacyEventBus;
  readonly serviceRegistry: LegacyServiceRegistry;
  readonly flowCanvasAdapterModule: LegacyFlowCanvasAdapterModule;
}

type DefaultModule<T> = {
  readonly default: T;
};

const legacyModulePaths = {
  httpClient: '/src/core/messaging/httpClient.js',
  webMessageBridge: '/src/core/messaging/webMessageBridge.js',
  eventBus: '/src/core/app/eventBus.js',
  serviceRegistry: '/src/core/app/serviceRegistry.js',
  flowCanvasAdapter: '/src/core/canvas/flowCanvasAdapter.js'
} as const;

export async function loadLegacyFrontendServices(): Promise<LegacyFrontendServices> {
  const [
    httpClientModule,
    webMessageBridgeModule,
    eventBusModule,
    serviceRegistryModule,
    flowCanvasAdapterModule
  ] = await Promise.all([
    import(/* @vite-ignore */ legacyModulePaths.httpClient) as Promise<DefaultModule<LegacyHttpClient>>,
    import(/* @vite-ignore */ legacyModulePaths.webMessageBridge) as Promise<DefaultModule<LegacyWebMessageBridge>>,
    import(/* @vite-ignore */ legacyModulePaths.eventBus) as Promise<DefaultModule<LegacyEventBus>>,
    import(/* @vite-ignore */ legacyModulePaths.serviceRegistry) as Promise<DefaultModule<LegacyServiceRegistry>>,
    import(/* @vite-ignore */ legacyModulePaths.flowCanvasAdapter) as Promise<LegacyFlowCanvasAdapterModule>
  ]);

  return {
    httpClient: httpClientModule.default,
    webMessageBridge: webMessageBridgeModule.default,
    eventBus: eventBusModule.default,
    serviceRegistry: serviceRegistryModule.default,
    flowCanvasAdapterModule
  };
}
