# Bridge Responsibility Contract

The Desktop WebView bridge and local HTTP APIs must expose the same business behavior without duplicating ownership.

## Ownership

| Responsibility | Owner |
| --- | --- |
| User/session state | Desktop auth/session services. |
| Project CRUD and flow save/load | Application services and HTTP endpoints. |
| File picker and shell integration | WebView bridge only. |
| Inspection run/stop/realtime control | Inspection controller and local API. |
| Realtime result push | Inspection SSE endpoints and event store. |
| AI flow generation | AI application services and HTTP endpoint wrappers. |
| Runtime package export | Runtime exporter plus path guard and endpoint role checks. |

## Bridge Rules

- Bridge messages must be request/response shaped and return a stable `success`, `errorCode`, `message` contract.
- New feature work should prefer HTTP endpoints unless the operation truly requires desktop shell access.
- WebView handlers may adapt payloads, but they must not become the source of schema truth.
- File-system writes must go through controlled paths or explicit user-selected paths.
- Errors should preserve both a user-readable message and a stable machine-readable code.

## Test Boundary

Contract tests should cover:

- project flow save/load through HTTP,
- equivalent WebView message handling for the same payload shape when a bridge path exists,
- role checks for admin/package/export paths,
- stable error codes for invalid paths, invalid flow payloads and unauthorized commands.
