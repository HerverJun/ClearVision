# OperatorLibrary Release Package Industrialization

Date: 2026-04-29

Scope: `Acme.OperatorLibrary` NuGet packaging, native runtime expectations, third-party notice/SBOM baseline, deployment matrix, and resource-lifecycle limits for ONNX/OCR/PLC/database operators.

## Package Contents

`Acme.OperatorLibrary` is a source-linked package around ClearVision operator code:

- `Acme.OperatorLibrary/src/**` for package-facing abstractions and module index.
- Source-linked operator/runtime code from `Acme.Product/src/Acme.Product.Contracts`, `Core`, `Infrastructure/Operators`, selected `Infrastructure` services, and `Acme.PlcComm`.
- Root package documentation: `README.md`, `THIRD-PARTY-NOTICES.md`, and `SBOM.md`.
- Symbols are emitted as `.snupkg` by `pack.ps1`.

The package does not include trained models, camera drivers, production database instances, PLC hardware configuration, or OCR model folders beyond what third-party runtime packages bring transitively.

## Restore Reproducibility

`Acme.OperatorLibrary.csproj` sets `RestorePackagesWithLockFile=true` so NuGet can materialize a package graph lock file during restore. First-time lock creation is a reviewable source change:

```powershell
dotnet restore Acme.OperatorLibrary/Acme.OperatorLibrary.csproj --use-lock-file
git diff -- Acme.OperatorLibrary/packages.lock.json
```

After `Acme.OperatorLibrary/packages.lock.json` is generated, reviewed, and checked in, release and CI restores must use locked mode:

```powershell
dotnet restore Acme.OperatorLibrary/Acme.OperatorLibrary.csproj --locked-mode
```

Do not publish from a restore that updates the lock file unexpectedly. This document records the reproducibility gate but does not generate packages or modify `packages.lock.json`.

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
| Serial/RTU/PLC | `System.IO.Ports`, `S7NetPlus`, `Acme.PlcComm` | Serial-port native assets may exist transitively, but Modbus RTU is documented as unsupported in the packaged operator. | Keep RTU disabled until serial-port lifecycle ownership is explicit. |

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

## Known Limitations

- Windows is the only fully documented native runtime profile for OpenCvSharp and Paddle OCR in this package.
- GPU ONNX acceleration is opportunistic; CPU fallback is expected when CUDA/TensorRT provider loading fails.
- Modbus RTU is not supported by the packaged `ModbusCommunicationOperator`.
- Static caches are process-local; multi-process hosts do not share ONNX/OCR/Modbus state.
- `S7NetPlus` `0.20.0` does not declare a license in its local nuspec and requires manual upstream review before external redistribution.
- The SBOM is currently Markdown generated from NuGet package restore output, not a formal CycloneDX or SPDX document.

## Release Checklist

1. Run `./analyze-deps.ps1` in `Acme.OperatorLibrary`.
2. Restore with `--locked-mode` after `Acme.OperatorLibrary/packages.lock.json` is checked in.
3. Run `./pack.ps1 -RunSmokeTest`.
4. Unpack the generated `.nupkg` and confirm `README.md`, `THIRD-PARTY-NOTICES.md`, and `SBOM.md` are present.
5. Confirm native runtime payloads match the deployment matrix.
6. Confirm no trained model or customer dataset is accidentally included.
7. Attach versioned accuracy evidence for any release note that claims model/OCR/measurement quality.
8. Attach external real-site evidence before claiming completed industrial validation.
9. Record unresolved license review items before publishing outside an internal feed.
