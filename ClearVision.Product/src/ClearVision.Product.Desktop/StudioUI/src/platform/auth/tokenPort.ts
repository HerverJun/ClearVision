export const authTokenStorageKey = 'cv_auth_token';

export interface AuthTokenPort {
  readToken(): string | undefined;
  setToken(token: string): void;
  removeToken(): void;
}

export interface AuthTokenStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export function createSessionStorageTokenPort(storage?: AuthTokenStorage): AuthTokenPort {
  return Object.freeze({
    readToken(): string | undefined {
      try {
        return storage?.getItem(authTokenStorageKey)?.trim() || undefined;
      } catch {
        return undefined;
      }
    },
    setToken(token: string): void {
      const normalized = token.trim();
      if (!normalized) throw new TypeError('Auth token must be non-empty.');
      storage?.setItem(authTokenStorageKey, normalized);
    },
    removeToken(): void {
      storage?.removeItem(authTokenStorageKey);
    }
  });
}

export function createMemoryTokenPort(initialToken?: string): AuthTokenPort {
  let token = initialToken?.trim() || undefined;
  return Object.freeze({
    readToken(): string | undefined {
      return token;
    },
    setToken(nextToken: string): void {
      const normalized = nextToken.trim();
      if (!normalized) throw new TypeError('Auth token must be non-empty.');
      token = normalized;
    },
    removeToken(): void {
      token = undefined;
    }
  });
}
