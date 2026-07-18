import { describe, expect, it, vi } from 'vitest';
import { authTokenStorageKey, createMemoryTokenPort, createSessionStorageTokenPort } from '@/platform/auth';

describe('Auth token port', () => {
  it('owns sessionStorage read, set and remove without localStorage', () => {
    const values = new Map<string, string>();
    const storage = {
      getItem: vi.fn((key: string) => values.get(key) ?? null),
      setItem: vi.fn((key: string, value: string) => { values.set(key, value); }),
      removeItem: vi.fn((key: string) => { values.delete(key); })
    };
    const port = createSessionStorageTokenPort(storage);

    port.setToken(' token-1 ');
    expect(port.readToken()).toBe('token-1');
    expect(storage.setItem).toHaveBeenCalledWith(authTokenStorageKey, 'token-1');

    port.removeToken();
    expect(port.readToken()).toBeUndefined();
    expect(storage.removeItem).toHaveBeenCalledWith(authTokenStorageKey);
  });

  it('provides the same narrow contract for tests and rejects empty writes', () => {
    const port = createMemoryTokenPort('initial');
    expect(port.readToken()).toBe('initial');
    expect(() => port.setToken('  ')).toThrow('non-empty');
    port.setToken('next');
    expect(port.readToken()).toBe('next');
    port.removeToken();
    expect(port.readToken()).toBeUndefined();
  });
});
