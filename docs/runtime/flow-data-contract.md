# FlowData Contract

This contract keeps frontend serialization, backend project persistence and runtime package export aligned.

## Flow Path

1. Frontend canvas serializes a flow document.
2. HTTP or bridge update receives `CanvasFlowDataDto` / `FlowDataDto` / `UpdateFlowRequest`.
3. `FlowEntityMapper` converts the DTO into project entities.
4. `JsonFileProjectFlowStorage` writes an atomic JSON snapshot with metadata hash and last-good recovery.
5. Runtime export reads the persisted flow, validates paths and emits a controlled package.

## Required Shape

| Field | Requirement |
| --- | --- |
| `nodes` / `operators` | Stable node/operator id, type, position and title. |
| `ports` | Direction, name and data type must be preserved. |
| `parameters` | Name, value and type must round-trip; missing required values remain validation errors. |
| `connections` | Source/target node and port ids must be stable. |
| `revision` / metadata hash | Used to detect stale or corrupted snapshots. |

## Legacy Shape Policy

Legacy DTO names may be accepted by adapters, but new persistence and runtime export code should normalize to one internal shape before validation. Contract tests should pin:

- node/operator compatibility,
- port and parameter round-trip,
- missing/invalid parameter errors,
- runtime export reading the same normalized shape.
