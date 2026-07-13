import { describe, expect, it } from 'vitest';
import { studioUiBuildMetadata } from '@/platform/diagnostics/buildMetadata';

describe('StudioUI build metadata', () => {
  it('keeps the production asset base explicit and immutable', () => {
    expect(studioUiBuildMetadata).toEqual({
      name: 'ClearVision StudioUI',
      version: '0.1.0',
      basePath: '/studio/',
      mode: 'test'
    });
    expect(Object.isFrozen(studioUiBuildMetadata)).toBe(true);
  });
});
