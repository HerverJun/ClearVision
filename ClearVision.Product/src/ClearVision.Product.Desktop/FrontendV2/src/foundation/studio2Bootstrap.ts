import { createPinia, type Pinia } from 'pinia';
import BuildPlaceholder from '@/components/BuildPlaceholder.vue';

export interface Studio2FrontendV2BuildInfo {
  readonly goal: 'G02A';
  readonly runtimeMounted: false;
  readonly authority: 'none';
}

export const studio2FrontendV2BuildInfo: Studio2FrontendV2BuildInfo = {
  goal: 'G02A',
  runtimeMounted: false,
  authority: 'none'
};

export const studio2BuildPlaceholderComponent = BuildPlaceholder;

export function createStudio2PiniaForFutureUiState(): Pinia {
  return createPinia();
}
