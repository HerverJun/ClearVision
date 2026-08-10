import { readonly, reactive, type DeepReadonly } from 'vue';

export type UiTheme = 'light' | 'dark';
export type UiDensity = 'compact' | 'comfortable';

export interface UiPreferencesProjection {
  readonly theme: UiTheme;
  readonly density: UiDensity;
  readonly reducedMotion: boolean;
  readonly rememberedUsername: string | null;
}

type MutableUiPreferencesProjection = {
  -readonly [Key in keyof UiPreferencesProjection]: UiPreferencesProjection[Key]
};

export interface UiPreferencesOwner {
  readonly projection: DeepReadonly<UiPreferencesProjection>;
  setTheme(theme: UiTheme): void;
  setDensity(density: UiDensity): void;
  setRememberedUsername(username: string | null): void;
  apply(): void;
  dispose(): void;
}

export interface UiPreferencesOwnerOptions {
  readonly storage?: Pick<Storage, 'getItem' | 'setItem'>;
  readonly root?: HTMLElement;
  readonly matchMedia?: (query: string) => MediaQueryList;
}

const storageKey = 'clearvision.studio-ui.preferences.v1';

function readStoredPreferences(
  storage?: Pick<Storage, 'getItem'>
): Pick<UiPreferencesProjection, 'theme' | 'density' | 'rememberedUsername'> {
  try {
    const raw = storage?.getItem(storageKey);
    if (!raw) return { theme: 'light', density: 'compact', rememberedUsername: null };
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    return {
      theme: parsed.theme === 'dark' ? 'dark' : 'light',
      density: parsed.density === 'comfortable' ? 'comfortable' : 'compact',
      rememberedUsername: typeof parsed.rememberedUsername === 'string' && parsed.rememberedUsername.length > 0
        ? parsed.rememberedUsername
        : null
    };
  } catch {
    return { theme: 'light', density: 'compact', rememberedUsername: null };
  }
}

export function createUiPreferencesOwner(
  options: UiPreferencesOwnerOptions = {}
): UiPreferencesOwner {
  const storage = options.storage ?? globalThis.localStorage;
  const root = options.root ?? globalThis.document?.documentElement;
  const media = (options.matchMedia ?? globalThis.matchMedia?.bind(globalThis))?.('(prefers-reduced-motion: reduce)');
  const stored = readStoredPreferences(storage);
  const state = reactive<MutableUiPreferencesProjection>({
    theme: stored.theme,
    density: stored.density,
    reducedMotion: media?.matches ?? false,
    rememberedUsername: stored.rememberedUsername
  });
  let disposed = false;

  function apply(): void {
    if (!root || disposed) return;
    root.dataset.theme = state.theme;
    root.dataset.density = state.density;
    root.dataset.reducedMotion = state.reducedMotion ? 'true' : 'false';
  }

  function persist(): void {
    try {
      storage?.setItem(storageKey, JSON.stringify({
        schemaVersion: 1,
        theme: state.theme,
        density: state.density,
        rememberedUsername: state.rememberedUsername
      }));
    } catch {
      // Preferences are optional UI projection; storage failures are non-fatal.
    }
  }

  const onMotionChange = (event: MediaQueryListEvent): void => {
    if (disposed) return;
    state.reducedMotion = event.matches;
    apply();
  };
  media?.addEventListener?.('change', onMotionChange);

  const owner: UiPreferencesOwner = Object.freeze({
    projection: readonly(state),
    setTheme(theme: UiTheme): void {
      if (disposed) return;
      state.theme = theme;
      persist();
      apply();
    },
    setDensity(density: UiDensity): void {
      if (disposed) return;
      state.density = density;
      persist();
      apply();
    },
    setRememberedUsername(username: string | null): void {
      if (disposed) return;
      state.rememberedUsername = username;
      persist();
    },
    apply,
    dispose(): void {
      if (disposed) return;
      disposed = true;
      media?.removeEventListener?.('change', onMotionChange);
    }
  });
  owner.apply();
  return owner;
}
