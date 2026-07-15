import { describe, expect, it, vi } from 'vitest';
import { createUiPreferencesOwner } from '@/app/preferences';

describe('uiPreferencesOwner', () => {
  it('defaults to compact light and persists valid preferences', () => {
    const values = new Map<string, string>();
    const root = document.createElement('html');
    const owner = createUiPreferencesOwner({
      root,
      storage: {
        getItem: key => values.get(key) ?? null,
        setItem: (key, value) => { values.set(key, value); }
      }
    });

    expect(owner.projection).toMatchObject({ theme: 'light', density: 'compact' });
    expect(root.dataset.density).toBe('compact');
    owner.setTheme('dark');
    owner.setDensity('comfortable');

    expect(root.dataset.theme).toBe('dark');
    expect(root.dataset.density).toBe('comfortable');
    expect([...values.values()][0]).toContain('comfortable');
    owner.dispose();
  });

  it('restores persisted preferences and releases reduced-motion listener', () => {
    let changeHandler: ((event: MediaQueryListEvent) => void) | undefined;
    const remove = vi.fn();
    const media = {
      matches: true,
      addEventListener: (_: string, handler: (event: MediaQueryListEvent) => void) => {
        changeHandler = handler;
      },
      removeEventListener: remove
    } as unknown as MediaQueryList;
    const root = document.createElement('html');
    const owner = createUiPreferencesOwner({
      root,
      storage: {
        getItem: () => JSON.stringify({ schemaVersion: 1, theme: 'dark', density: 'comfortable' }),
        setItem: () => undefined
      },
      matchMedia: () => media
    });

    expect(root.dataset.theme).toBe('dark');
    expect(root.dataset.reducedMotion).toBe('true');
    changeHandler?.({ matches: false } as MediaQueryListEvent);
    expect(root.dataset.reducedMotion).toBe('false');
    owner.dispose();
    expect(remove).toHaveBeenCalledOnce();
  });
});
