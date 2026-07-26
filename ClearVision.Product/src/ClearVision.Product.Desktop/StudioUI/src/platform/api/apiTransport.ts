import {
  ApiAbortError,
  ApiBadRequestError,
  ApiConfigurationError,
  ApiConflictError,
  ApiDecodeError,
  ApiForbiddenError,
  type ApiHttpError,
  type ApiHttpErrorDetails,
  ApiNetworkError,
  ApiNotFoundError,
  ApiRequestPathError,
  ApiServerError,
  ApiUnauthorizedError,
  ApiUnexpectedHttpError
} from './errors';

export type ApiTokenProvider = () => string | null | undefined;

export interface ApiUnauthorizedContext {
  readonly method: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  readonly path: string;
  readonly url: string;
  readonly sessionGeneration: number;
}

export type ApiUnauthorizedHandler = (context: ApiUnauthorizedContext) => void | Promise<void>;

export interface ApiGetOptions {
  readonly signal?: AbortSignal;
  readonly suppressUnauthorizedHandler?: boolean;
}

export interface ApiWriteOptions extends ApiGetOptions {
  readonly headers?: Readonly<Record<string, string>>;
}

export interface ApiBlobResponse {
  readonly blob: Blob;
  readonly contentType: string;
  readonly contentLength: number;
  readonly etag: string | null;
  readonly sha256: string | null;
  readonly headers: Headers;
}

export interface ApiTextStreamResponse {
  readonly stream: ReadableStream<Uint8Array>;
  readonly headers: Headers;
}

export interface ApiTransport {
  readonly apiBaseUrl: string;
  setUnauthorizedHandler?(
    handler: ApiUnauthorizedHandler | null,
    sessionGenerationProvider?: () => number
  ): void;
  get<T = unknown>(path: string, options?: ApiGetOptions): Promise<T | undefined>;
  getTextStream?(path: string, options?: ApiGetOptions): Promise<ApiTextStreamResponse>;
  post?<T = unknown>(path: string, body: unknown, options?: ApiWriteOptions): Promise<T | undefined>;
  postBlob?(path: string, body: unknown, options?: ApiWriteOptions): Promise<ApiBlobResponse>;
  put?<T = unknown>(path: string, body: unknown, options?: ApiWriteOptions): Promise<T | undefined>;
  patch?<T = unknown>(path: string, body: unknown, options?: ApiWriteOptions): Promise<T | undefined>;
  getBlob?(path: string, options?: ApiGetOptions): Promise<ApiBlobResponse>;
  delete?(path: string, options?: ApiWriteOptions): Promise<void>;
}

export interface CreateApiTransportOptions {
  readonly apiBaseUrl: string;
  readonly tokenProvider?: ApiTokenProvider;
  readonly expectedOrigin?: string;
  readonly onUnauthorized?: ApiUnauthorizedHandler;
  readonly sessionGenerationProvider?: () => number;
}

const allowedRootPaths = new Set(['/health']);

function isLoopbackHostname(hostname: string): boolean {
  const normalized = hostname.toLowerCase();
  if (normalized === 'localhost' || normalized === '[::1]') {
    return true;
  }

  const ipv4Parts = normalized.split('.');
  return ipv4Parts.length === 4
    && ipv4Parts[0] === '127'
    && ipv4Parts.every(part => /^\d{1,3}$/.test(part) && Number(part) <= 255);
}

function parseLoopbackHttpUrl(value: string, label: string): URL {
  let url: URL;
  try {
    url = new URL(value);
  } catch (error) {
    throw new ApiConfigurationError(`${label} must be an absolute HTTP(S) URL: ${String(error)}`);
  }

  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw new ApiConfigurationError(`${label} must use HTTP or HTTPS.`);
  }

  if (!isLoopbackHostname(url.hostname)) {
    throw new ApiConfigurationError(`${label} must use a loopback hostname.`);
  }

  if (url.username || url.password) {
    throw new ApiConfigurationError(`${label} must not contain credentials.`);
  }

  return url;
}

function resolveExpectedOrigin(injectedOrigin?: string): URL {
  const runtimeOrigin = injectedOrigin ?? globalThis.location?.origin;
  if (!runtimeOrigin || runtimeOrigin === 'null') {
    throw new ApiConfigurationError(
      'An HTTP(S) page origin is required to validate the injected API base URL.'
    );
  }

  const origin = parseLoopbackHttpUrl(runtimeOrigin, 'Expected origin');
  if (origin.pathname !== '/' || origin.search || origin.hash) {
    throw new ApiConfigurationError('Expected origin must not include a path, query, or fragment.');
  }

  return origin;
}

function validateApiBaseUrl(value: string, expectedOrigin?: string): URL {
  const apiBaseUrl = parseLoopbackHttpUrl(value, 'API base URL');
  const origin = resolveExpectedOrigin(expectedOrigin);

  if (apiBaseUrl.origin !== origin.origin) {
    throw new ApiConfigurationError('API base URL must be same-origin with the StudioUI page.');
  }

  if (apiBaseUrl.search || apiBaseUrl.hash) {
    throw new ApiConfigurationError('API base URL must not include a query or fragment.');
  }

  const normalizedPath = apiBaseUrl.pathname.replace(/\/+$/, '');
  if (normalizedPath !== '/api') {
    throw new ApiConfigurationError('API base URL path must be /api.');
  }

  apiBaseUrl.pathname = '/api/';
  return apiBaseUrl;
}

function assertSafeRequestPath(path: string): string {
  if (!path || path !== path.trim()) {
    throw new ApiRequestPathError(path, 'API request path must be non-empty and contain no surrounding whitespace.');
  }

  if (path.includes('\\')) {
    throw new ApiRequestPathError(path, 'API request path must not contain backslashes.');
  }

  const pathname = path.split(/[?#]/, 1)[0] ?? '';
  if (/%(?:2f|5c)/i.test(pathname)) {
    throw new ApiRequestPathError(path, 'API request path must not contain encoded path separators.');
  }

  let decodedPathname: string;
  try {
    decodedPathname = decodeURIComponent(pathname);
  } catch {
    throw new ApiRequestPathError(path, 'API request path contains invalid percent encoding.');
  }

  if (decodedPathname.split('/').includes('..')) {
    throw new ApiRequestPathError(path, 'API request path must not contain parent-directory segments.');
  }

  if (path.startsWith('//') || /^[a-z][a-z\d+.-]*:/i.test(path)) {
    throw new ApiRequestPathError(path, 'Absolute and protocol-relative API request URLs are forbidden.');
  }

  return path;
}

function resolveRequestUrl(apiBaseUrl: URL, path: string): URL {
  const safePath = assertSafeRequestPath(path);
  const rootRelative = safePath.startsWith('/');
  const requestUrl = rootRelative
    ? new URL(safePath, apiBaseUrl.origin)
    : new URL(safePath, apiBaseUrl);

  if (requestUrl.origin !== apiBaseUrl.origin) {
    throw new ApiRequestPathError(path, 'API request URL must remain same-origin.');
  }

  if (requestUrl.hash) {
    throw new ApiRequestPathError(path, 'API request URL must not contain a fragment.');
  }

  if (rootRelative) {
    if (!allowedRootPaths.has(requestUrl.pathname)) {
      throw new ApiRequestPathError(path, 'Only the public /health endpoint may use a root-relative path.');
    }
    return requestUrl;
  }

  if (!requestUrl.pathname.startsWith(apiBaseUrl.pathname)) {
    throw new ApiRequestPathError(path, 'API request path must remain inside the injected /api/ base.');
  }

  return requestUrl;
}

function isAbortFailure(error: unknown, signal?: AbortSignal): boolean {
  if (signal?.aborted) {
    return true;
  }

  return typeof error === 'object' && error !== null && 'name' in error && error.name === 'AbortError';
}

function decodeHttpErrorPayload(body: string): unknown {
  const trimmed = body.trim();
  if (!trimmed) {
    return undefined;
  }

  try {
    return JSON.parse(trimmed) as unknown;
  } catch {
    return body;
  }
}

function createHttpError(response: Response, url: string, body: string): ApiHttpError {
  const details: ApiHttpErrorDetails = {
    url,
    status: response.status,
    statusText: response.statusText,
    payload: decodeHttpErrorPayload(body),
    responseBody: body
  };

  switch (response.status) {
    case 400:
      return new ApiBadRequestError(details);
    case 401:
      return new ApiUnauthorizedError(details);
    case 403:
      return new ApiForbiddenError(details);
    case 404:
      return new ApiNotFoundError(details);
    case 409:
      return new ApiConflictError(details);
    default:
      return response.status >= 500
        ? new ApiServerError(details)
        : new ApiUnexpectedHttpError(details);
  }
}

export function createApiTransport(options: CreateApiTransportOptions): ApiTransport {
  const apiBaseUrl = validateApiBaseUrl(options.apiBaseUrl, options.expectedOrigin);
  const tokenProvider = options.tokenProvider ?? (() => undefined);
  let unauthorizedHandler = options.onUnauthorized ?? null;
  let sessionGenerationProvider = options.sessionGenerationProvider ?? (() => 0);

  function requestHeaders(
    accept: string,
    additions: Readonly<Record<string, string>> = {}
  ): Record<string, string> {
    const headers: Record<string, string> = { Accept: accept, ...additions };
    const token = tokenProvider()?.trim();
    if (token) headers.Authorization = `Bearer ${token}`;
    return headers;
  }

  async function send(
    path: string,
    method: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE',
    requestOptions: ApiGetOptions = {},
    body?: BodyInit,
    headers: Readonly<Record<string, string>> = {}
  ): Promise<Readonly<{ response: Response; url: string }>> {
    const requestUrl = resolveRequestUrl(apiBaseUrl, path);
    const url = requestUrl.toString();
    if (requestOptions.signal?.aborted) {
      throw new ApiAbortError(url, requestOptions.signal.reason);
    }

    const requestInit: RequestInit = {
      method,
      headers,
      cache: 'no-store',
      credentials: 'same-origin',
      redirect: 'error'
    };
    if (requestOptions.signal) requestInit.signal = requestOptions.signal;
    if (body !== undefined) requestInit.body = body;

    try {
      return Object.freeze({
        response: await globalThis.fetch(url, requestInit),
        url
      });
    } catch (error) {
      if (isAbortFailure(error, requestOptions.signal)) {
        throw new ApiAbortError(url, error);
      }
      throw new ApiNetworkError(url, error);
    }
  }

  async function readText(response: Response, url: string, signal?: AbortSignal): Promise<string> {
    try {
      return await response.text();
    } catch (error) {
      if (isAbortFailure(error, signal)) throw new ApiAbortError(url, error);
      throw new ApiNetworkError(url, error);
    }
  }

  async function readJson<T>(
    response: Response,
    url: string,
    signal: AbortSignal | undefined,
    request: Readonly<{
      method: ApiUnauthorizedContext['method'];
      path: string;
      suppressUnauthorizedHandler: boolean;
    }>
  ): Promise<T | undefined> {
    const body = await readText(response, url, signal);
    if (!response.ok) {
      const error = createHttpError(response, url, body);
      if (response.status === 401 && !request.suppressUnauthorizedHandler && unauthorizedHandler) {
        await unauthorizedHandler(Object.freeze({
          method: request.method,
          path: request.path,
          url,
          sessionGeneration: sessionGenerationProvider()
        }));
      }
      throw error;
    }
    if (response.status === 204 || response.status === 205 || !body.trim()) return undefined;
    try {
      return JSON.parse(body) as T;
    } catch (error) {
      throw new ApiDecodeError(url, response.status, error);
    }
  }

  return Object.freeze({
    apiBaseUrl: apiBaseUrl.toString().replace(/\/$/, ''),
    setUnauthorizedHandler(
      handler: ApiUnauthorizedHandler | null,
      generationProvider: () => number = () => 0
    ): void {
      unauthorizedHandler = handler;
      sessionGenerationProvider = generationProvider;
    },
    async get<T = unknown>(path: string, requestOptions: ApiGetOptions = {}): Promise<T | undefined> {
      const { response, url } = await send(
        path,
        'GET',
        requestOptions,
        undefined,
        requestHeaders('application/json')
      );
      return readJson<T>(response, url, requestOptions.signal, {
        method: 'GET',
        path,
        suppressUnauthorizedHandler: requestOptions.suppressUnauthorizedHandler === true
      });
    },
    async getTextStream(
      path: string,
      requestOptions: ApiGetOptions = {}
    ): Promise<ApiTextStreamResponse> {
      const { response, url } = await send(
        path,
        'GET',
        requestOptions,
        undefined,
        requestHeaders('text/event-stream')
      );
      if (!response.ok) {
        const body = await readText(response, url, requestOptions.signal);
        const error = createHttpError(response, url, body);
        if (response.status === 401 && !requestOptions.suppressUnauthorizedHandler && unauthorizedHandler) {
          await unauthorizedHandler(Object.freeze({
            method: 'GET',
            path,
            url,
            sessionGeneration: sessionGenerationProvider()
          }));
        }
        throw error;
      }
      if (!response.body) {
        throw new ApiNetworkError(url, new Error('The response did not provide a readable stream.'));
      }
      return Object.freeze({ stream: response.body, headers: response.headers });
    },
    async post<T = unknown>(
      path: string,
      body: unknown,
      requestOptions: ApiWriteOptions = {}
    ): Promise<T | undefined> {
      const { response, url } = await send(
        path,
        'POST',
        requestOptions,
        JSON.stringify(body),
        requestHeaders('application/json', {
          'Content-Type': 'application/json',
          ...requestOptions.headers
        })
      );
      return readJson<T>(response, url, requestOptions.signal, {
        method: 'POST',
        path,
        suppressUnauthorizedHandler: requestOptions.suppressUnauthorizedHandler === true
      });
    },
    async postBlob(
      path: string,
      body: unknown,
      requestOptions: ApiWriteOptions = {}
    ): Promise<ApiBlobResponse> {
      const { response, url } = await send(
        path,
        'POST',
        requestOptions,
        JSON.stringify(body),
        requestHeaders('application/octet-stream, image/*, application/json', {
          'Content-Type': 'application/json',
          ...requestOptions.headers
        })
      );
      if (!response.ok) {
        const responseBody = await readText(response, url, requestOptions.signal);
        const error = createHttpError(response, url, responseBody);
        if (response.status === 401 && !requestOptions.suppressUnauthorizedHandler && unauthorizedHandler) {
          await unauthorizedHandler(Object.freeze({
            method: 'POST',
            path,
            url,
            sessionGeneration: sessionGenerationProvider()
          }));
        }
        throw error;
      }
      let blob: Blob;
      try {
        blob = await response.blob();
      } catch (error) {
        if (isAbortFailure(error, requestOptions.signal)) throw new ApiAbortError(url, error);
        throw new ApiNetworkError(url, error);
      }
      return Object.freeze({
        blob,
        contentType: response.headers.get('Content-Type')?.split(';', 1)[0]?.trim().toLowerCase() || blob.type,
        contentLength: blob.size,
        etag: response.headers.get('ETag'),
        sha256: response.headers.get('X-Artifact-Sha256'),
        headers: response.headers
      });
    },
    async put<T = unknown>(
      path: string,
      body: unknown,
      requestOptions: ApiWriteOptions = {}
    ): Promise<T | undefined> {
      const { response, url } = await send(
        path,
        'PUT',
        requestOptions,
        JSON.stringify(body),
        requestHeaders('application/json', {
          'Content-Type': 'application/json',
          ...requestOptions.headers
        })
      );
      return readJson<T>(response, url, requestOptions.signal, {
        method: 'PUT',
        path,
        suppressUnauthorizedHandler: requestOptions.suppressUnauthorizedHandler === true
      });
    },
    async patch<T = unknown>(
      path: string,
      body: unknown,
      requestOptions: ApiWriteOptions = {}
    ): Promise<T | undefined> {
      const { response, url } = await send(
        path,
        'PATCH',
        requestOptions,
        JSON.stringify(body),
        requestHeaders('application/json', {
          'Content-Type': 'application/json',
          ...requestOptions.headers
        })
      );
      return readJson<T>(response, url, requestOptions.signal, {
        method: 'PATCH',
        path,
        suppressUnauthorizedHandler: requestOptions.suppressUnauthorizedHandler === true
      });
    },
    async getBlob(path: string, requestOptions: ApiGetOptions = {}): Promise<ApiBlobResponse> {
      const { response, url } = await send(
        path,
        'GET',
        requestOptions,
        undefined,
        requestHeaders('application/octet-stream, image/*, application/json')
      );
      if (!response.ok) {
        const body = await readText(response, url, requestOptions.signal);
        const error = createHttpError(response, url, body);
        if (response.status === 401 && !requestOptions.suppressUnauthorizedHandler && unauthorizedHandler) {
          await unauthorizedHandler(Object.freeze({
            method: 'GET',
            path,
            url,
            sessionGeneration: sessionGenerationProvider()
          }));
        }
        throw error;
      }
      let blob: Blob;
      try {
        blob = await response.blob();
      } catch (error) {
        if (isAbortFailure(error, requestOptions.signal)) throw new ApiAbortError(url, error);
        throw new ApiNetworkError(url, error);
      }
      return Object.freeze({
        blob,
        contentType: response.headers.get('Content-Type')?.split(';', 1)[0]?.trim().toLowerCase() || blob.type,
        contentLength: blob.size,
        etag: response.headers.get('ETag'),
        sha256: response.headers.get('X-Artifact-Sha256'),
        headers: response.headers
      });
    },
    async delete(path: string, requestOptions: ApiWriteOptions = {}): Promise<void> {
      const { response, url } = await send(
        path,
        'DELETE',
        requestOptions,
        undefined,
        requestHeaders('application/json', requestOptions.headers)
      );
      const body = await readText(response, url, requestOptions.signal);
      if (!response.ok) {
        const error = createHttpError(response, url, body);
        if (response.status === 401 && !requestOptions.suppressUnauthorizedHandler && unauthorizedHandler) {
          await unauthorizedHandler(Object.freeze({
            method: 'DELETE',
            path,
            url,
            sessionGeneration: sessionGenerationProvider()
          }));
        }
        throw error;
      }
    }
  } satisfies ApiTransport);
}
