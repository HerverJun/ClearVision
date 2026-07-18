import { describe, expect, it } from 'vitest';
import { decodeSessionProjection, sessionIdentityOf } from '@/app/session';

describe('auth-owned session projection contract', () => {
  it('decodes required fields and preserves unknown role strings as UI projection only', () => {
    const user = decodeSessionProjection({
      userId: 'u-1',
      username: 'operator',
      role: 'FutureRole',
      extra: true
    });

    expect(user).toEqual({ userId: 'u-1', username: 'operator', role: 'FutureRole' });
    expect(sessionIdentityOf(user)).toBe('u-1\u0000operator\u0000FutureRole');
    expect(() => decodeSessionProjection({ username: 'missing-id', role: 'Admin' })).toThrow();
  });
});
