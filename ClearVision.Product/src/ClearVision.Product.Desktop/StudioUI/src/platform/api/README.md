# API boundary

`apiTransport.ts` is the only production request core. It accepts the injected `/api` base URL, exposes GET only, attaches an optional bearer token, supports cancellation, and classifies transport, decode, and HTTP status failures.

The transport performs no port discovery, retry, redirect-to-login, response cache, EventSource work, or business writes. Relative paths stay under `/api/`; the public root `/health` endpoint is the only root-relative exception.
