# Shared boundary

Only capability-neutral, authority-free helpers may be added here.

`inspectionOutcome.ts` is the shared, read-only projection for the backend Execution/Decision
dual axis and the nine canonical inspection outcome kinds. Station and Results may consume it,
but capability-specific DTO decoders, stores, queries and lifecycle owners remain private.
