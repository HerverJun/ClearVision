/// <reference types="vite/client" />

import type { StudioUiLifecycleDiagnostics } from './platform/diagnostics/studioUiLifecycleDiagnostics';

declare global {
  const __STUDIO_UI_BUILD__: Readonly<{
    name: string;
    version: string;
    basePath: '/studio/';
    mode: string;
  }>;

  interface Window {
    readonly __STUDIO_UI_READY__?: boolean;
    readonly __STUDIO_UI_DIAGNOSTICS__?: StudioUiLifecycleDiagnostics;
  }
}
