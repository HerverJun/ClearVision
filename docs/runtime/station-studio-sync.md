# Studio-Station Sync SOP

## Scope And Guardrails

Studio-Station sync is an opt-in LAN monitoring path for Station telemetry. Station always initiates outbound connections to Studio and keeps local runtime autonomy when Studio is offline.

Current-stage hard constraints:

- Do not transfer images. Station sync DTOs and SignalR messages carry result summaries, health, logs, commands, cursors, and package metadata only. They must not carry source images, output images, thumbnails, base64 image data, or `byte[]` image blobs.
- Do not add a Station HTTP server. Station uses SignalR Client and HTTP Client to reach Studio.
- Do not introduce MQTT, Kafka, ElasticSearch, or other large external brokers.
- Studio ingress and Station sync are disabled by default.

## Studio Configuration

Studio reads the `StationIngress` section.

```json
"StationIngress": {
  "Enabled": false,
  "ListenMode": "Loopback",
  "Port": 5000,
  "SharedToken": "",
  "AllowInsecureDevelopment": false,
  "AllowMessagePack": true,
  "OfflineThresholdSeconds": 15,
  "ResultBufferPerStation": 200,
  "EventBufferSize": 1000,
  "HealthBufferPerStation": 100,
  "LogBufferPerStation": 100,
  "CommandBufferPerStation": 100
}
```

Loopback dry run:

1. Set `StationIngress:Enabled=true`.
2. Keep `StationIngress:ListenMode=Loopback`.
3. Set a non-empty `StationIngress:SharedToken`, or use `AllowInsecureDevelopment=true` only in local development.
4. Start Studio and open the Station monitor page.

LAN trial:

1. Set `StationIngress:Enabled=true`.
2. Set `StationIngress:ListenMode=Lan`.
3. Set `StationIngress:Port` to the approved LAN port.
4. Set a strong, shared `StationIngress:SharedToken`.
5. Keep `AllowInsecureDevelopment=false`.
6. Confirm firewall rules allow inbound Studio traffic on the selected port.

Studio accepts machine ingress at `/hubs/station-ingest`. Package downloads use Studio HTTP endpoints and the same Station shared token.

## Station Configuration

Station reads the `StationSync` section.

```json
"StationSync": {
  "Enabled": false,
  "StudioBaseUrl": "http://127.0.0.1:5000",
  "StudioHubUrl": "",
  "SharedToken": "",
  "HeartbeatIntervalSeconds": 5,
  "HealthIntervalSeconds": 15,
  "SnapshotDebounceMilliseconds": 750,
  "PendingBatchSize": 100,
  "MaxBufferedResults": 10000,
  "SpoolDirectory": "%LocalAppData%\\ClearVisionStation\\spool",
  "MaxSpoolMb": 512,
  "MaxSpoolDays": 7,
  "OutboundQueueCapacity": 1000,
  "LogQueueCapacity": 500,
  "MaxLogSummariesPerMinute": 60,
  "LogDirectory": "%LocalAppData%\\ClearVisionStation\\logs",
  "MaxCollectLogsMb": 64,
  "MaxCollectLogsHours": 24,
  "PackageDirectory": "%LocalAppData%\\ClearVisionStation\\packages"
}
```

Use either:

- `StudioBaseUrl`, for example `http://studio-host:5000`; Station resolves this to `/hubs/station-ingest`.
- `StudioHubUrl`, for an explicit full hub URL. This takes precedence over `StudioBaseUrl`.

Set `StationSync:SharedToken` to exactly match Studio `StationIngress:SharedToken`.

Station identity:

- If no `StationId` is saved locally, Station generates one from machine name plus a random suffix and persists it in the local Station settings store.
- Operators can set `StationName` and `LineName` from the Station UI; these values are persisted locally and sent during registration, heartbeat, health, and result messages.
- Do not reuse a `StationId` across physical Stations.

## Operations

Online status:

1. Start Studio with ingress enabled.
2. Start Station with sync enabled and matching token.
3. Open the Station monitor.
4. Confirm the Station appears online with recent heartbeat and health timestamps.

Offline handling:

1. Stop Studio or disconnect the LAN.
2. Station inspection must continue locally.
3. Result summaries are queued and spooled within `MaxSpoolMb`, `MaxSpoolDays`, and `MaxBufferedResults`.
4. Restart Studio or restore LAN.
5. Station reconnects, re-registers, and replays unacknowledged summaries in sequence.
6. Studio ACKs only persisted cursor state; duplicates by `StationId + SequenceId` must not create duplicate records.

Remote package deploy:

1. Upload or create the package in Studio.
2. Issue `DeployPackage` from the Station monitor.
3. Studio records the command and Station polls it; Studio does not call Station directly.
4. Station downloads the package from Studio over HTTP. Large package bytes are not sent over SignalR.
5. Station verifies download `sha256`.
6. Station extracts to staging.
7. Station validates `manifest.json`, `packageId`, manifest `sha256`, and `minStationVersion`.
8. Station promotes staging without directly overwriting active content.
9. On load failure, Station keeps or restores last-known-good active package.
10. Deployment status and failures are reported back to Studio command records and audit.

Rollback:

1. Use the Station monitor command flow to deploy the previous known-good package, or restore the local last-known-good package if the active package fails to load.
2. Confirm Station reports the expected package version in health/status.
3. Check command status and audit records in Studio.

Logs and diagnostics:

- Station keeps full local logs under `StationSync:LogDirectory`.
- Station only relays bounded WARN/ERROR/FATAL summaries to Studio.
- Summary relay uses `LogQueueCapacity`, `MaxLogSummariesPerMinute`, short-window dedupe, truncation, and token scrubbing.
- `CollectLogs` is command-driven and bounded by `MaxCollectLogsMb` and `MaxCollectLogsHours`; it creates a local diagnostic bundle and reports the local bundle path/status back through command completion.
- Do not use log relay for full real-time log streaming.

## REST And SignalR Surface

Machine-to-machine ingress:

- `SignalR /hubs/station-ingest`

Studio monitor REST/SSE:

- `GET /api/stations`
- `GET /api/stations/summary`
- `GET /api/stations/{stationId}`
- `GET /api/stations/{stationId}/results`
- `GET /api/stations/{stationId}/health`
- `GET /api/stations/{stationId}/logs`
- `GET /api/stations/{stationId}/commands`
- `POST /api/stations/{stationId}/commands`
- `POST /api/stations/{stationId}/deploy-package`
- `PATCH /api/stations/{stationId}/identity`
- `GET /api/stations/audit`
- `GET /api/stations/statistics`
- `GET /api/stations/events`
- `GET /api/station-packages`
- `POST /api/station-packages/test`

## Simulator

Run Studio with Station ingress enabled, then start simulated Stations:

```powershell
& "./scripts/run-station-simulator.ps1" `
  -Studio "http://127.0.0.1:5000" `
  -Token "dev-shared-token" `
  -Stations 16 `
  -Rate 2 `
  -NgRate 0.08 `
  -ErrorRate 0.01 `
  -LogRate 0.05 `
  -DisconnectRate 0.01 `
  -DurationSeconds 300
```

Simulator behavior:

- Registers virtual Stations.
- Sends heartbeat and health snapshots.
- Sends result summaries only, with no images.
- Sends bounded WARN/ERROR log summaries.
- Polls commands and reports command lifecycle states.
- Randomly disconnects and reconnects when `DisconnectRate` is set.

## Validation Checklist

1. Restore: `dotnet restore Acme.Product/Acme.Product.sln`.
2. Build: `dotnet build Acme.Product/Acme.Product.sln --no-restore`.
3. Product tests: `& "./scripts/run-dotnet-test-serial.ps1" -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj"`.
4. Desktop tests: `& "./scripts/run-dotnet-test-serial.ps1" -Project "Acme.Product/tests/Acme.Product.Desktop.Tests/Acme.Product.Desktop.Tests.csproj"`.
5. Total test entry: `dotnet test Acme.Product/Acme.Product.sln`.
6. Start Studio with ingress enabled and token configured.
7. Start Station or the simulator with the same token.
8. Confirm Station monitor shows overview, station list, recent results, health, alerts, logs, commands, and SSE updates.
9. Stop Studio while Station keeps inspecting; confirm summaries spool locally.
10. Restart Studio; confirm replay ACK advances only through persisted contiguous sequences.
11. Send the same result summary 10 times; confirm only one persisted result.
12. Produce at least 20 results while disconnected; confirm replay after reconnect.
13. Restart Studio and Station during replay; confirm cursor and sequence resume.
14. Force spool over configured cap in a non-production sandbox; confirm oldest bounded behavior and no runtime crash.
15. Issue Ping, ReloadPackage, Stop/Start, DeployPackage, and CollectLogs commands; confirm states, command results, and audit records.

## Review Risks

- Package deployment must be rechecked with real `.cvpkg` layouts from production export.
- SQLite schema creation is additive and migration-light; create formal migrations before release.
- Health collection is best-effort for device-level signals; do not let camera or PLC probes block inspection.
- Log relay is summary-only by design. Full log retrieval remains bounded and command-driven.
- Future image support requires a separate design and must not be slipped into Station sync DTOs.
