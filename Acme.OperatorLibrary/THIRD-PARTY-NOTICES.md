# Third-Party Notices

This package is a redistribution wrapper for ClearVision operator code and the NuGet dependencies declared by `Acme.OperatorLibrary.csproj`.

Release owners must verify third-party notices against the exact `.nupkg` contents before external publication. This file records the package-intent baseline for `Acme.OperatorLibrary` `1.0.2`.

## Direct NuGet Dependencies

| Package | Version | Declared license in local nuspec | Release note |
| --- | ---: | --- | --- |
| Microsoft.Data.SqlClient | 5.2.2 | MIT | Includes SQL Server client runtime assets and SNI native assets transitively. |
| Microsoft.Data.Sqlite | 8.0.0 | MIT | Pulls SQLitePCLRaw/e_sqlite3 native runtime assets transitively. |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.0 | MIT | Managed dependency. |
| Microsoft.Extensions.Logging.Abstractions | 10.0.0 | MIT | Managed dependency. |
| Microsoft.ML.OnnxRuntime | 1.17.0 | package license file | Carries ONNX Runtime native assets for multiple RIDs; review package `LICENSE` and native notices. |
| Microsoft.ML.OnnxRuntime.Managed | 1.17.0 | package license file | Managed ONNX Runtime API companion; review package `LICENSE.txt`. |
| MySqlConnector | 2.3.7 | MIT | Managed MySQL client. |
| NModbus | 3.0.81 | package license file | Modbus TCP dependency; review package `LICENSE.txt`. |
| OpenCvSharp4 | 4.9.0.20240103 | Apache-2.0 | Managed OpenCvSharp bindings. |
| OpenCvSharp4.runtime.win | 4.9.0.20240103 | Apache-2.0 | Windows OpenCV native runtime payload. |
| PaddleOCRSharp | 4.4.0.1 | Apache-2.0 | OCR wrapper; pulls Paddle runtime assets transitively. |
| S7NetPlus | 0.20.0 | not declared in nuspec | Must be manually reviewed against upstream repository before external redistribution. |
| System.Drawing.Common | 8.0.11 | MIT | Windows-oriented drawing dependency. |
| System.IO.Ports | 8.0.0 | MIT | Serial-port dependency and native runtime assets transitively. |
| ZXing.Net | 0.16.9 | Apache-2.0 | Barcode decoding dependency. |

## Native Runtime Payloads To Review

- `onnxruntime` native libraries from `Microsoft.ML.OnnxRuntime`.
- `OpenCvSharpExtern` and OpenCV FFmpeg/video native DLLs from `OpenCvSharp4.runtime.win`.
- `PaddleOCR.dll` and Paddle native runtime assets from `PaddleOCRSharp` / `Paddle.Runtime.win_x64`.
- `e_sqlite3` native libraries from SQLitePCLRaw.
- `Microsoft.Data.SqlClient.SNI` native libraries.
- `System.IO.Ports` native libraries for Linux/macOS profiles.

## Release Gate

Before publishing outside an internal feed:

1. Generate the exact package with `./pack.ps1 -RunSmokeTest`.
2. Unpack the `.nupkg` and enumerate `lib/`, `runtimes/`, and root notice files.
3. Compare the unpacked native payload with this notice and `SBOM.md`.
4. Attach the generated dependency report from `./analyze-deps.ps1`.
5. Record any manually reviewed license text that is not represented by SPDX expressions in NuGet metadata.
