# Operator Library Read-Only Audit Baseline

- Schema: 2026-07-10.operator-library-readonly-audit.v1
- Audit mode: read-only
- Baseline SHA: dc542913b132a5e788dfdde60fbb25e253483612
- Generated UTC: 2026-07-10T10:59:27.6597302Z

The audit reads checked-in catalog, enum, operator source, documentation, tests, and package boundary files. It does not instantiate or execute operators, read images/models, access network or hardware, or mutate audit inputs.

## Summary

| Metric | Value |
| --- | ---: |
| catalogOperatorCount | 158 |
| catalogDeclaredTotalCount | 158 |
| operatorEnumMemberCount | 162 |
| operatorSourceFileCount | 173 |
| operatorTestFileCount | 152 |
| documentationFileCount | 164 |
| duplicateCatalogIdCount | 0 |
| catalogOnlyIdCount | 0 |
| enumOnlyIdCount | 4 |
| missingDocumentationCount | 0 |
| missingSourceReferenceCount | 0 |
| missingTestReferenceCount | 3 |
| contractCheckCount | 3 |
| contractFailureCount | 0 |
| findingCount | 1 |

## Contract Checks

| Check | Passed | Details |
| --- | :---: | --- |
| BlobAnalysis/BlobLabeling typed list contract | yes | BlobAnalysis.Blobs=BlobList; BlobAnalysis.BlobFeatures=BlobFeatureList; BlobLabeling.Blobs=BlobList; legacy feature path documented=True |
| Region source contract | yes | BinaryImageToRegion.Region=Region; RectangleRegion.inputPorts=0; RectangleRegion.Rectangle=Rectangle |
| OperatorLibrary package read-only boundary | yes | Package project, module index, smoke index, SBOM, and third-party notices are present. |

## Coverage Gaps

| Kind | Count | Sample |
| --- | ---: | --- |
| enum-only (legacy/internal candidates) | 4 | GaussianBlur, ModbusRtuCommunication, OnnxInference, Preprocessing |
| missing documentation | 0 |  |
| missing source reference | 0 |  |
| missing test reference | 3 | MitsubishiMcCommunication, OmronFinsCommunication, PyramidShapeMatch |

## Findings

- Catalog operators without an indexed unit-test reference: MitsubishiMcCommunication, OmronFinsCommunication, PyramidShapeMatch

## Safety Boundary

- Metadata only: True
- Operator execution: False
- Real resources touched: False
- Source/catalog inputs mutated: False
