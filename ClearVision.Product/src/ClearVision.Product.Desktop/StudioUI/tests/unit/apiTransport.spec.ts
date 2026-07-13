import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  ApiAbortError,
  ApiBadRequestError,
  ApiConfigurationError,
  ApiConflictError,
  ApiDecodeError,
  ApiForbiddenError,
  ApiNetworkError,
  ApiNotFoundError,
  ApiRequestPathError,
  ApiServerError,
  ApiUnauthorizedError,
  ApiUnexpectedHttpError,
  createApiTransport
} from '@/platform/api';

const apiBaseUrl = 'http://localhost:5000/api';
const expectedOrigin = 'http://localhost:5000';

function createFetchMock(): ReturnType<typeof vi.fn> {
  const fetchMock = vi.fn();
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('createApiTransport', () => {
  it('accepts an explicit same-origin IPv4 loopback base', () => {
    expect(() => createApiTransport({
      apiBaseUrl: 'https://127.23.45.67:7443/api',
      expectedOrigin: 'https://127.23.45.67:7443'
    })).not.toThrow();
  });

  it('sends relative GET requests under /api with the current bearer token', async () => {
    const fetchMock = createFetchMock();
    fetchMock.mockImplementation(() => Promise.resolve(new Response(
      JSON.stringify({ requiresInitialAdminSetup: false }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' }
      }
    )));
    let token = ' token-one ';
    const transport = createApiTransport({
      apiBaseUrl,
      expectedOrigin,
      tokenProvider: () => token
    });
    const controller = new AbortController();

    await expect(transport.get<{ requiresInitialAdminSetup: boolean }>('auth/setup-status', {
      signal: controller.signal
    })).resolves.toEqual({ requiresInitialAdminSetup: false });

    const firstCall = fetchMock.mock.calls[0];
    expect(firstCall?.[0]).toBe('http://localhost:5000/api/auth/setup-status');
    expect(firstCall?.[1]).toMatchObject({
      method: 'GET',
      cache: 'no-store',
      credentials: 'same-origin',
      redirect: 'error',
      signal: controller.signal
    });
    expect(new Headers(firstCall?.[1]?.headers).get('Accept')).toBe('application/json');
    expect(new Headers(firstCall?.[1]?.headers).get('Authorization')).toBe('Bearer token-one');

    token = 'token-two';
    await transport.get('health');
    const secondCall = fetchMock.mock.calls[1];
    expect(new Headers(secondCall?.[1]?.headers).get('Authorization')).toBe('Bearer token-two');
    expect(transport.apiBaseUrl).toBe(apiBaseUrl);
  });

  it('allows only the public /health root-relative endpoint', async () => {
    const fetchMock = createFetchMock();
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ Status: 'Healthy' }), { status: 200 }));
    const transport = createApiTransport({ apiBaseUrl, expectedOrigin });

    await expect(transport.get('/health?detail=true')).resolves.toEqual({ Status: 'Healthy' });
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5000/health?detail=true',
      expect.objectContaining({ method: 'GET' })
    );

    await expect(transport.get('/auth/setup-status')).rejects.toBeInstanceOf(ApiRequestPathError);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('returns undefined for 204 and empty successful bodies', async () => {
    const fetchMock = createFetchMock();
    fetchMock
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response('', { status: 200 }));
    const transport = createApiTransport({ apiBaseUrl, expectedOrigin });

    await expect(transport.get('first')).resolves.toBeUndefined();
    await expect(transport.get('second')).resolves.toBeUndefined();
  });

  it('omits Authorization when the token provider has no token', async () => {
    const fetchMock = createFetchMock();
    fetchMock.mockResolvedValue(new Response('{}', { status: 200 }));
    const transport = createApiTransport({
      apiBaseUrl,
      expectedOrigin,
      tokenProvider: () => '   '
    });

    await transport.get('health');

    const headers = new Headers(fetchMock.mock.calls[0]?.[1]?.headers);
    expect(headers.has('Authorization')).toBe(false);
  });

  it.each([
    [400, ApiBadRequestError, 'bad-request'],
    [401, ApiUnauthorizedError, 'unauthorized'],
    [403, ApiForbiddenError, 'forbidden'],
    [404, ApiNotFoundError, 'not-found'],
    [409, ApiConflictError, 'conflict'],
    [503, ApiServerError, 'server'],
    [418, ApiUnexpectedHttpError, 'unexpected-http-status']
  ] as const)('classifies HTTP %i without retrying', async (status, ErrorType, kind) => {
    const fetchMock = createFetchMock();
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ error: `status-${status}` }), {
      status,
      statusText: 'Test status',
      headers: { 'Content-Type': 'application/json' }
    }));
    const transport = createApiTransport({ apiBaseUrl, expectedOrigin });

    const request = transport.get('failure');
    await expect(request).rejects.toBeInstanceOf(ErrorType);
    await expect(request).rejects.toMatchObject({
      status,
      kind,
      payload: { error: `status-${status}` }
    });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('classifies invalid successful JSON as a decode error', async () => {
    const fetchMock = createFetchMock();
    fetchMock.mockResolvedValue(new Response('<html>not json</html>', { status: 200 }));
    const transport = createApiTransport({ apiBaseUrl, expectedOrigin });

    await expect(transport.get('health')).rejects.toBeInstanceOf(ApiDecodeError);
  });

  it('classifies fetch failures as network errors without retrying', async () => {
    const fetchMock = createFetchMock();
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    const transport = createApiTransport({ apiBaseUrl, expectedOrigin });

    await expect(transport.get('health')).rejects.toBeInstanceOf(ApiNetworkError);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('classifies cancellation and avoids starting an already-aborted request', async () => {
    const fetchMock = createFetchMock();
    const transport = createApiTransport({ apiBaseUrl, expectedOrigin });
    const controller = new AbortController();
    controller.abort('test cancellation');

    await expect(transport.get('health', { signal: controller.signal }))
      .rejects.toBeInstanceOf(ApiAbortError);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ['https://example.com/api', 'https://example.com'],
    ['http://localhost:5001/api', expectedOrigin],
    ['ftp://localhost:5000/api', expectedOrigin],
    ['http://localhost:5000/other', expectedOrigin],
    ['http://user:password@localhost:5000/api', expectedOrigin]
  ])('rejects unsafe injected base URL %s', (unsafeBaseUrl, origin) => {
    expect(() => createApiTransport({ apiBaseUrl: unsafeBaseUrl, expectedOrigin: origin }))
      .toThrow(ApiConfigurationError);
  });

  it.each([
    'https://localhost:5000/api/health',
    '//localhost:5000/api/health',
    '../health',
    'auth/../../health',
    'auth/%2e%2e/%2e%2e/health',
    'auth/%2e%2e%2f%2e%2e%2fhealth',
    'auth\\setup-status',
    'health#fragment',
    '/api/health'
  ])('rejects unsafe request path %s before fetch', async path => {
    const fetchMock = createFetchMock();
    const transport = createApiTransport({ apiBaseUrl, expectedOrigin });

    await expect(transport.get(path)).rejects.toBeInstanceOf(ApiRequestPathError);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
