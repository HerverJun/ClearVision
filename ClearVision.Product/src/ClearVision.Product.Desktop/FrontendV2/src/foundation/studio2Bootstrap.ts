import { createPinia, type Pinia } from 'pinia';
import BuildPlaceholder from '@/components/BuildPlaceholder.vue';
export {
  disposeStudio2FoundationIsland,
  getActiveStudio2FoundationIsland,
  mountStudio2FoundationIsland
} from '@/foundation/studio2FoundationIsland';

export interface Studio2FrontendV2BuildInfo {
  readonly goal: 'G04B';
  readonly runtimeMounted: 'controlled-by-host-flag';
  readonly authority: 'none';
}

export const studio2FrontendV2BuildInfo: Studio2FrontendV2BuildInfo = {
  goal: 'G04B',
  runtimeMounted: 'controlled-by-host-flag',
  authority: 'none'
};

export const studio2BuildPlaceholderComponent = BuildPlaceholder;

export function createStudio2PiniaForFutureUiState(): Pinia {
  return createPinia();
}
