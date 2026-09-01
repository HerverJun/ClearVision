# OperatorLibrary Release Package Industrialization

Date: 2026-04-29; Wave 3D provenance and target-evidence contract updated 2026-09-01

Scope: `ClearVision.OperatorLibrary` NuGet packaging, native runtime expectations, third-party notice/SBOM baseline, deployment matrix, and resource-lifecycle limits for ONNX/OCR/PLC/database operators.

## Package Contents

`ClearVision.OperatorLibrary` is a source-linked package around ClearVision operator code:

- `ClearVision.OperatorLibrary/src/**` for package-facing abstractions and module index.
- Source-linked operator/runtime code from `ClearVision.Product/src/ClearVision.Product.Contracts`, `Core`, `Infrastructure/Operators`, selected `Infrastructure` services, and `ClearVision.PlcComm`.
- Root package documentation: `README.md`, `THIRD-PARTY-NOTICES.md`, and `SBOM.md`.
- Symbols are emitted as `.snupkg` by `pack.ps1`.

The package does not include trained models, camera drivers, production database instances, PLC hardware configuration, or OCR model folders beyond what third-party runtime packages bring transitively.

## Restore Reproducibility

`ClearVision.OperatorLibrary.csproj` sets `RestorePackagesWithLockFile=true` so NuGet can materialize a package graph lock file during restore. First-time lock creation is a reviewable source change:

```powershell
dotnet restore ClearVision.OperatorLibrary/ClearVision.OperatorLibrary.csproj --use-lock-file
git diff -- ClearVision.OperatorLibrary/packages.lock.json
```

After `ClearVision.OperatorLibrary/packages.lock.json` is generated, reviewed, and checked in, release and CI restores must use locked mode:

```powershell
dotnet restore ClearVision.OperatorLibrary/ClearVision.OperatorLibrary.csproj --locked-mode
```

Do not publish from a restore that updates the lock file unexpectedly. This document records the reproducibility gate but does not generate packages or modify `packages.lock.json`.

## Wave 3C/3D Canonical Portable And Supply-Chain Contract

The only portable implementation is `scripts/package-portable-deployment.ps1`. Studio/Station wrappers and the tag Release workflow call that implementation; PR/main raw build artifacts are diagnostic and are not field portable packages. The tag-default field profile is `Studio / win-x64 / field-self-contained`; `diagnostic-framework-dependent` has a different artifact name and support boundary.

The official portable profile forces `EnableHuaraySdk=false`. Ignored, machine-local Huaray files must never make the package differ according to the build host; the Huaray runtime is a separately provisioned and approved site prerequisite. Publish hygiene and the package self-test reject `MVSDK_Net.dll`, Node/node_modules, FrontendV2, source/test/debug assets, development manifests, secrets, and machine-local absolute/user paths.

For each final portable ZIP and OperatorLibrary nupkg, `quality/tools/generate_release_supply_chain.py` derives and validates:

- `SBOM.spdx.json`;
- `THIRD-PARTY-NOTICES.txt`;
- `dependency-report.json` and `dependency-report.md`;
- `identity-manifest.json`, `SHA256SUMS`, vulnerability status, and validation summary.

These release artifacts bind Git SHA/dirty, SDK/runtime inventory, RID/profile, generator/version/time, final package hashes, and input checksums. The Markdown `SBOM.md` and notice material carried by the nupkg remain seed/documentation inputs only; they are not the formal final-package SBOM or redistribution evidence.

The machine-readable policy is `quality/policies/release-supply-chain-policy.json`. Unknown/denied licenses and vulnerability findings fail closed unless a package+version exception has reason, Owner, approval basis, and expiry. A network advisory source failure is recorded as `unavailable` with vulnerability count `null`, never as zero vulnerabilities.

Wave 3C implementation SHA `fc1f11bc605a8c9b4e16ba78af4de62457b27802` produced a 79-component SPDX/report set from the actual final ZIP/nupkg. Structural generation and artifact consistency passed, while release eligibility stayed false for AutoMapper 15.1.3 and Microsoft.Data.SqlClient.SNI.runtime 5.2.0 unapproved terms; BCrypt.Net-Next 4.0.3, HslCommunication 12.7.0, and S7NetPlus 0.20.0 `NOASSERTION`; and an unavailable vulnerability source. No exception Owner/approval/expiry was fabricated.

Wave 3D replaces that package as the current local candidate. Implementation SHA `66b7ef2a69d56e4d63155700b05a4aaa2af80c03` produced `ClearVision-Studio-1.0.3-wave3d.1-66b7ef2a-win-x64-field-self-contained.zip` (`291,339,455` bytes, SHA-256 `4cb02239ffab4d3e0dc4fc29d129c87c37959a05115b2b20ccadb466cadfd5e9`, content fingerprint `sha256:8cbd5f66265cca2914c40c7aaed5b685b596870fd2f8aa601de89b628b93ef69`) and OperatorLibrary nupkg (`1,422,390` bytes, SHA-256 `68093e7726497e9d1dbd1ecb42810b69b8d6e9df65a4312e6cfb6aeb2206e9e3`). Publish hygiene, installed smoke `47/47`, final-artifact SBOM/notices/report/identity/checksums, package-result self-test, structural generation, and artifact consistency passed. The Wave 3C files remain historical evidence and are not the current final package.

`collect_license_provenance.py` now binds each requested dependency to the exact final nupkg bytes, its nuspec/license file, official NuGet registration/catalog metadata, the corresponding upstream tag/commit and LICENSE hash, retrieval time, ETag/revision, and the final ZIP/nupkg hashes. Current dispositions are: AutoMapper 15.1.3 `IDENTIFIED / REVIEW_REQUIRED` (`RPL-1.5 OR LicenseRef-LuckyPenny-Commercial`); Microsoft.Data.SqlClient.SNI.runtime 5.2.0 `IDENTIFIED / REVIEW_REQUIRED` (the packaged SNI terms take precedence over the repository MIT file); BCrypt.Net-Next 4.0.3 `IDENTIFIED / POLICY_ALLOWED` (MIT); HslCommunication 12.7.0 `CONFLICTING_EVIDENCE / REVIEW_REQUIRED` because the package has commercial restrictions and no version-bound 12.7.0 repository tag; and S7NetPlus 0.20.0 `IDENTIFIED / POLICY_ALLOWED` (MIT, tag `v0.20.0`, commit `f1ae0ea084e712b59e414de6aaee7d196244a239`). Identification never substitutes for policy approval.

The Wave 3D advisory merge queried NuGet audit advisory sources and OSV for the final 79-package identity set. OSV was available at `2026-09-01T11:43:06.184336Z`, database revision `2026-06-18T13:26:15.158508Z`, and preserved `GHSA-2m69-gcr7-jv3q` for `SQLitePCLRaw.lib.e_sqlite3@2.1.6` with unknown severity. NuGet audit was unavailable, so the aggregate remains `scanStatus=unavailable` and `vulnerabilityCount=null`; the OSV finding and both source records remain in the machine-readable report. An empty or partial source never becomes a zero-vulnerability claim.

## Native Runtime Matrix

| Area | Dependency | Runtime expectation | Release action |
| --- | --- | --- | --- |
| ONNX detection | `Microsoft.ML.OnnxRuntime` | CPU native runtime is included by NuGet runtime assets. CUDA/TensorRT paths are optional and require machine-level GPU runtime compatibility. | Validate CPU on every package smoke run. Validate GPU only on matching NVIDIA/CUDA/TensorRT hosts. |
| OpenCV image processing | `OpenCvSharp4.runtime.win` | Windows native OpenCV runtime assets are included. Non-Windows OpenCvSharp native runtime is not declared by this package. | Treat Windows as the supported native profile unless additional runtime packages are added. |
| OCR | `PaddleOCRSharp`, `Paddle.Runtime.win_x64` | Paddle OCR native runtime is Windows x64 oriented. Engine initialization is lazy and process-local. | Validate the target machine has compatible VC++/native runtime dependencies. |
| SQLite writes | `Microsoft.Data.Sqlite`, SQLitePCLRaw | `e_sqlite3` native assets are pulled transitively. | Validate write access and file locking on the deployment filesystem. |
| SQL Server writes | `Microsoft.Data.SqlClient` | SNI native assets are pulled transitively. | Validate TLS/authentication policy against the target server. |
| MySQL writes | `MySqlConnector` | Managed client. | Validate connection string and server timeout policy. |
| Modbus TCP | `NModbus` | Managed TCP client over `TcpClient`; this package serializes requests per endpoint. | Validate against physical or simulated PLC endpoints. |
| Serial/RTU/PLC | `System.IO.Ports`, `S7NetPlus`, `ClearVision.PlcComm` | Serial-port native assets may exist transitively, but Modbus RTU is documented as unsupported in the packaged operator. | Keep RTU disabled until serial-port lifecycle ownership is explicit. |

## Resource Lifecycle

### ONNX

- `DeepLearningOperator` uses a static LRU cache limited to three model sessions and leases sessions while inference is active.
- `DeepLearningOperator.UnloadModel(modelPath)` marks matching sessions for disposal after active leases complete.
- PatchCore ONNX embeddings now detect model file replacement by comparing file length and last-write time; stale sessions are removed and disposed before reloading.
- PatchCore ONNX embeddings expose an internal cache clear hook for host/test shutdown.

### OCR

- `OcrEngineProvider` lazily initializes a single `PaddleOCREngine`.
- `DetectText` is serialized through the provider lock, which isolates PaddleOCRSharp from concurrent engine calls.
- `GetEngine()` returns the raw engine and should be treated as an advanced escape hatch; callers that bypass `DetectText` own concurrency safety.

### Modbus / PLC

- `ModbusCommunicationOperator` pools TCP connections by `ip:port`.
- Requests to the same endpoint are serialized through a per-endpoint operation lock because Modbus request/response streams are not safe for concurrent use on one TCP connection.
- `TimeoutMs` bounds connection wait and socket send/receive timeouts. When IO, socket, timeout, or protocol exceptions occur, the pooled endpoint is purged before the next attempt.
- Modbus RTU remains a documented limitation for the package operator because serial-port lifecycle, exclusive ownership, and reconnect policy are not yet represented in operator metadata.

### Database

- `DatabaseWriteOperator` opens and disposes a database connection per execution.
- Table creation uses a per database/table semaphore and a success cache; failed table initialization is not cached.
- Commands use a five-second timeout and retry transient provider exceptions up to three attempts.
- Table names are restricted to letters, digits, and underscores to avoid identifier injection; values use provider parameters.

## Versioned Accuracy Statement

Version baseline `1.0.2`:

- Deep learning detection accuracy is model-dependent. The package preserves ONNX model metadata, label validation, target class validation, and provenance outputs, but it does not certify a universal precision/recall level.
- OCR accuracy depends on PaddleOCRSharp runtime, OCR model assets, image quality, language coverage, and deployment CPU characteristics. The package only guarantees lazy initialization, serialized execution via `DetectText`, and disposal semantics.
- Measurement/calibration accuracy follows the operator implementation and calibration artifacts supplied by the host. Calibration folders and camera geometry are not embedded in the package.
- Communication/database operators are integration operators. Their correctness is bounded by endpoint configuration, network stability, schema permissions, and timeout policy.

Any release claiming a numerical accuracy target must attach the model/catalog version, label contract, evaluation dataset, hardware profile, and quality report ID.

External real production-site data, customer sign-off, and line sign-off are release blockers that cannot be closed by local repository work. Local synthetic, dataset, benchmark, smoke, and field-substitute replay evidence may support regression confidence, but it must not be described as completed industrial validation.

## Plugin Manifest And Maturity

External operator packages must ship an operator plugin manifest before they are loaded by a host. The baseline contract is documented in `docs/operator-library/plugin-manifest.md`, with `operator-plugin-manifest.sample.json` as the package-author example.

Operator maturity is explicit:

- `Delivered`: implemented, tested, documented, and visible by default for supported profiles.
- `Experimental`: usable but still requiring scenario validation or feature flags.
- `PlaceholderDisabled`: metadata/contract exists but runtime integration is disabled; the operator must not be enabled by default.

`MqttPublishOperator` is intentionally tagged as `maturity:placeholder-disabled` until an MQTT client dependency, connection lifecycle, and CI/smoke evidence are added.

## Known Limitations

- Windows is the only fully documented native runtime profile for OpenCvSharp and Paddle OCR in this package.
- GPU ONNX acceleration is opportunistic; CPU fallback is expected when CUDA/TensorRT provider loading fails.
- Modbus RTU is not supported by the packaged `ModbusCommunicationOperator`.
- Static caches are process-local; multi-process hosts do not share ONNX/OCR/Modbus state.
- `S7NetPlus` `0.20.0` still has no license declaration in its nuspec, but Wave 3D authoritative evidence binds the exact package hash to upstream tag `v0.20.0`, commit `f1ae0ea084e712b59e414de6aaee7d196244a239`, and MIT `License.txt` hash `c4dec632bd494dbb1328e58aa783b95ccbded9a7ae16f84a5562df74c6500601`. It is policy-allowed only because MIT is explicitly allowed; the package/version/hash evidence must remain attached.
- AutoMapper and Microsoft SNI remain unapproved, and HslCommunication remains conflicting/review-required. Do not invent an Owner, exception, approval reference, or expiry to clear them.
- Formal release SBOM is SPDX JSON derived from the actual final ZIP/nupkg. In-package Markdown remains seed/documentation and must not be presented as the final release SBOM.

## Release Checklist

1. Run `./analyze-deps.ps1` in `ClearVision.OperatorLibrary`.
2. Restore with `--locked-mode` after `ClearVision.OperatorLibrary/packages.lock.json` is checked in.
3. Run `./pack.ps1 -RunSmokeTest`; the installed smoke must preserve package-public 156, catalog population/fingerprint/metadata, no Desktop/runtime execution-host dependency, and disabled-operator rejection.
4. Unpack the generated `.nupkg` and confirm `README.md`, `THIRD-PARTY-NOTICES.md`, and `SBOM.md` are present.
5. Confirm native runtime payloads match the deployment matrix.
6. Confirm no trained model or customer dataset is accidentally included.
7. Attach versioned accuracy evidence for any release note that claims model/OCR/measurement quality.
8. Attach external real-site evidence before claiming completed industrial validation.
9. Run canonical portable packaging with the approved RID/profile, validate final-package SPDX/notices/dependency/identity/checksums, and enforce `quality/policies/release-supply-chain-policy.json`.
10. Do not publish while license/vulnerability policy reports blockers; do not turn unavailable advisory data into a zero-vulnerability claim.
11. Validate the operator plugin manifest against the host version and operator contract before exposing package operators in the catalog.
12. Ship the G16/U07 evidence kit beside Release assets, never inside the product runtime directory; target profiles remain `NOT_RUN` until a real operator records the required machine, performance, workflow, screenshot/log hash, and sign-off fields.
