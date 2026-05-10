# Realtime Frontend Communication

Inspection and result pages use the same realtime semantics.

## Inspection SSE

Endpoint:

```text
GET /api/inspection/realtime/{projectId}/events
```

Events:

| Event | Meaning |
| --- | --- |
| `resultProduced` | A persisted or runtime-produced inspection result summary. |
| `heartbeat` | Connection keepalive. |

Clients should use browser `EventSource`. Browser-managed reconnect is allowed. When a client supplies `Last-Event-ID`, the server replays available history from the in-memory event store. Replay capacity is bounded by server configuration.

## Result Page

The result panel subscribes to the inspection SSE endpoint for the active project. It no longer uses the old `/hub/inspection-results` placeholder.

## Slow Consumers

Each SSE connection uses a bounded channel. Slow consumers may miss events instead of forcing the runtime or persistence path to block. Diagnostics are exposed at:

```text
GET /api/inspection/realtime/diagnostics
```

## History Fallback

Pages that need complete history must call the history endpoint and treat SSE as an incremental live update stream. SSE replay is a convenience window, not the source of truth for full historical pagination.
