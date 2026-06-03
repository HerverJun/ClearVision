# Operator Dependency Report

> GeneratedAt: `2026-05-09T00:06:55.7507291+08:00`
> OperatorFiles: **169**

## Key Host Dependency Types
| Type | Category | MatchCount | FileCount | Recommendation |
|------|----------|-----------:|----------:|---------------|
| `OperatorBase` | Infrastructure | 156 | 154 | Keep a lightweight base class in package layer. |
| `ImageWrapper` | Infrastructure | 330 | 93 | Keep image abstraction stable; isolate memory strategy by profile. |
| `OperatorExecutionOutput` | Core | 972 | 158 | Expose host-agnostic execution result model for package consumers. |
| `OperatorMetadata` | Core | 0 | 0 | Expose metadata DTO/contract and adapter. |
| `PortDefinition` | Core | 0 | 0 | Expose port DTO/contract and adapter. |
| `Operator` | Core | 404 | 159 | Wrap runtime operator entity behind request contract if host isolation is required. |

## Namespace Usage (Top 20)
| Namespace | RefCount |
|-----------|---------:|
| `ClearVision.Product.Core.Entities` | 160 |
| `ClearVision.Product.Core.Enums` | 159 |
| `ClearVision.Product.Core.Operators` | 159 |
| `ClearVision.Product.Core.Attributes` | 156 |
| `ClearVision.Product.Core.ValueObjects` | 60 |
| `ClearVision.Product.Infrastructure.Calibration` | 12 |
| `ClearVision.Product.Infrastructure.ImageProcessing` | 8 |
| `ClearVision.Product.Core.Services` | 6 |
| `ClearVision.Product.Infrastructure.Memory` | 6 |
| `ClearVision.Product.Infrastructure.AI.Runtime` | 4 |
| `ClearVision.Product.Infrastructure.Services` | 4 |
| `ClearVision.Product.Infrastructure.PointCloud.Filters` | 2 |
| `ClearVision.Product.Infrastructure.PointCloud.Segmentation` | 2 |
| `ClearVision.Product.Core.Cameras` | 1 |
| `ClearVision.Product.Core.Streaming` | 1 |
| `ClearVision.Product.Infrastructure.AI.Anomaly` | 1 |
| `ClearVision.Product.Infrastructure.Cameras` | 1 |
| `ClearVision.Product.Infrastructure.Operators` | 1 |
| `ClearVision.Product.Infrastructure.Operators.DatabaseWrite` | 1 |
| `ClearVision.Product.Infrastructure.PointCloud.Features` | 1 |

## Notes
- This report is generated from `ClearVision.Product.Infrastructure/Operators/*.cs`.
- MatchCount is text-pattern based and intended for migration prioritization, not semantic compilation truth.
- For Phase 3.3, use this report together with abstraction adapters under `ClearVision.OperatorLibrary/src`.
