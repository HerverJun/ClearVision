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
| `Acme.Product.Core.Entities` | 160 |
| `Acme.Product.Core.Enums` | 159 |
| `Acme.Product.Core.Operators` | 159 |
| `Acme.Product.Core.Attributes` | 156 |
| `Acme.Product.Core.ValueObjects` | 60 |
| `Acme.Product.Infrastructure.Calibration` | 12 |
| `Acme.Product.Infrastructure.ImageProcessing` | 8 |
| `Acme.Product.Core.Services` | 6 |
| `Acme.Product.Infrastructure.Memory` | 6 |
| `Acme.Product.Infrastructure.AI.Runtime` | 4 |
| `Acme.Product.Infrastructure.Services` | 4 |
| `Acme.Product.Infrastructure.PointCloud.Filters` | 2 |
| `Acme.Product.Infrastructure.PointCloud.Segmentation` | 2 |
| `Acme.Product.Core.Cameras` | 1 |
| `Acme.Product.Core.Streaming` | 1 |
| `Acme.Product.Infrastructure.AI.Anomaly` | 1 |
| `Acme.Product.Infrastructure.Cameras` | 1 |
| `Acme.Product.Infrastructure.Operators` | 1 |
| `Acme.Product.Infrastructure.Operators.DatabaseWrite` | 1 |
| `Acme.Product.Infrastructure.PointCloud.Features` | 1 |

## Notes
- This report is generated from `Acme.Product.Infrastructure/Operators/*.cs`.
- MatchCount is text-pattern based and intended for migration prioritization, not semantic compilation truth.
- For Phase 3.3, use this report together with abstraction adapters under `Acme.OperatorLibrary/src`.

