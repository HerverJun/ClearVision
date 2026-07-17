export type ApiRequestErrorCode =
  | 'configuration'
  | 'request-path'
  | 'network'
  | 'abort'
  | 'decode';

export type ApiHttpErrorKind =
  | 'bad-request'
  | 'unauthorized'
  | 'forbidden'
  | 'not-found'
  | 'conflict'
  | 'server'
  | 'unexpected-http-status';

export class ApiConfigurationError extends Error {
  readonly code = 'configuration' as const;

  constructor(message: string) {
    super(message);
    this.name = 'ApiConfigurationError';
  }
}

export class ApiRequestPathError extends Error {
  readonly code = 'request-path' as const;
  readonly path: string;

  constructor(path: string, message: string) {
    super(message);
    this.name = 'ApiRequestPathError';
    this.path = path;
  }
}

export class ApiNetworkError extends Error {
  readonly code = 'network' as const;
  readonly url: string;

  constructor(url: string, cause: unknown) {
    super(`Request to ${url} failed before a response was available.`, { cause });
    this.name = 'ApiNetworkError';
    this.url = url;
  }
}

export class ApiAbortError extends Error {
  readonly code = 'abort' as const;
  readonly url: string;

  constructor(url: string, cause?: unknown) {
    super(`Request to ${url} was aborted.`, { cause });
    this.name = 'ApiAbortError';
    this.url = url;
  }
}

export class ApiDecodeError extends Error {
  readonly code = 'decode' as const;
  readonly url: string;
  readonly status: number;

  constructor(url: string, status: number, cause: unknown) {
    super(`Response from ${url} could not be decoded as JSON.`, { cause });
    this.name = 'ApiDecodeError';
    this.url = url;
    this.status = status;
  }
}

export interface ApiHttpErrorDetails {
  readonly url: string;
  readonly status: number;
  readonly statusText: string;
  readonly payload: unknown;
  readonly responseBody: string;
}

export class ApiHttpError extends Error {
  readonly kind: ApiHttpErrorKind;
  readonly url: string;
  readonly status: number;
  readonly statusText: string;
  readonly payload: unknown;
  readonly responseBody: string;

  protected constructor(kind: ApiHttpErrorKind, details: ApiHttpErrorDetails) {
    const suffix = details.statusText ? ` ${details.statusText}` : '';
    super(`Request to ${details.url} failed with HTTP ${details.status}${suffix}.`);
    this.name = 'ApiHttpError';
    this.kind = kind;
    this.url = details.url;
    this.status = details.status;
    this.statusText = details.statusText;
    this.payload = details.payload;
    this.responseBody = details.responseBody;
  }
}

export class ApiBadRequestError extends ApiHttpError {
  constructor(details: ApiHttpErrorDetails) {
    super('bad-request', details);
    this.name = 'ApiBadRequestError';
  }
}

export class ApiUnauthorizedError extends ApiHttpError {
  constructor(details: ApiHttpErrorDetails) {
    super('unauthorized', details);
    this.name = 'ApiUnauthorizedError';
  }
}

export class ApiForbiddenError extends ApiHttpError {
  constructor(details: ApiHttpErrorDetails) {
    super('forbidden', details);
    this.name = 'ApiForbiddenError';
  }
}

export class ApiNotFoundError extends ApiHttpError {
  constructor(details: ApiHttpErrorDetails) {
    super('not-found', details);
    this.name = 'ApiNotFoundError';
  }
}

export class ApiConflictError extends ApiHttpError {
  constructor(details: ApiHttpErrorDetails) {
    super('conflict', details);
    this.name = 'ApiConflictError';
  }
}

export class ApiServerError extends ApiHttpError {
  constructor(details: ApiHttpErrorDetails) {
    super('server', details);
    this.name = 'ApiServerError';
  }
}

export class ApiUnexpectedHttpError extends ApiHttpError {
  constructor(details: ApiHttpErrorDetails) {
    super('unexpected-http-status', details);
    this.name = 'ApiUnexpectedHttpError';
  }
}
