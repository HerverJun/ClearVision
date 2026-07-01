export interface ClearVisionStartupConfig {
  readonly workspaceV2Enabled: boolean;
  readonly apiBaseUrl: string;
  readonly hostKind: string;
  readonly frontendV2BasePath: string;
}

declare global {
  interface Window {
    readonly __CLEARVISION_STARTUP__?: ClearVisionStartupConfig;
    readonly __API_BASE_URL__?: string;
  }
}

export function readStartupConfig(windowRef: Window = window): ClearVisionStartupConfig {
  const injected = windowRef.__CLEARVISION_STARTUP__;
  if (injected) {
    return injected;
  }

  return {
    workspaceV2Enabled: false,
    apiBaseUrl: windowRef.__API_BASE_URL__ ?? '',
    hostKind: 'desktop-webview2',
    frontendV2BasePath: '/v2'
  };
}
