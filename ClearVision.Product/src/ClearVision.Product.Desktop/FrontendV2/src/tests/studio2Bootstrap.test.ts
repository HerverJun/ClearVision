import { describe, expect, it } from 'vitest';
import {
  createStudio2PiniaForFutureUiState,
  studio2BuildPlaceholderComponent,
  studio2FrontendV2BuildInfo
} from '@/foundation/studio2Bootstrap';

describe('Studio2 FrontendV2 G02A build foundation', () => {
  it('does not mount a runtime root or claim business authority', () => {
    expect(studio2FrontendV2BuildInfo).toEqual({
      goal: 'G02A',
      runtimeMounted: false,
      authority: 'none'
    });
  });

  it('keeps Pinia available only as a future UI-state tool', () => {
    const pinia = createStudio2PiniaForFutureUiState();
    expect(pinia.state.value).toEqual({});
  });

  it('compiles a Vue placeholder without mounting it', () => {
    expect(studio2BuildPlaceholderComponent).toBeTruthy();
    expect(studio2BuildPlaceholderComponent.__name).toBe('BuildPlaceholder');
  });
});
