# Operator Plugin Manifest

Status: baseline contract for external operator packaging and host compatibility checks.

## Goals

- Make third-party capabilities explicit before they appear in the operator catalog.
- Separate delivered, experimental, and placeholder-disabled integrations.
- Give the host one deterministic place to validate schema, package version, host version, native profile, and operator contract version.

## Maturity Levels

| Level | Meaning | Catalog behavior |
| --- | --- | --- |
| `Delivered` | The operator is implemented, tested, documented, and enabled by default for supported profiles. | Visible by default. |
| `Experimental` | The operator is usable but still requires scenario-specific validation or feature flags. | Visible with an experimental marker. |
| `PlaceholderDisabled` | The operator contract exists, but runtime integration is not enabled. | Hidden by default or shown only with a disabled/experimental marker. |

`MqttPublishOperator` is currently `PlaceholderDisabled`: it validates the contract and fails fast instead of pretending to publish.

## Manifest Fields

| Field | Required | Notes |
| --- | --- | --- |
| `schemaVersion` | yes | Current value: `1.0`. |
| `pluginId` | yes | Stable reverse-DNS or package-style identifier. |
| `packageId` / `packageVersion` | yes | NuGet/package identity and SemVer-compatible version. |
| `hostCompatibility.minHostVersion` | yes | Minimum ClearVision host version. |
| `hostCompatibility.maxHostVersion` | no | Optional upper bound for breaking host transitions. |
| `hostCompatibility.operatorContractVersion` | yes | Current value: `1.0`. |
| `nativeProfiles` | yes | Example: `win-x64`. |
| `capabilities` | yes | Example: `operators`, `metadata`, `native-runtime`. |
| `operators[]` | yes | Operator-level type name, runtime type, maturity, default visibility, and tags. |

## Host Check

Use `OperatorPluginManifestCompatibility.Evaluate(...)` before loading an external package. The compatibility check blocks:

- unsupported manifest schema versions,
- invalid or incompatible host version ranges,
- operator contract mismatches,
- empty operator declarations,
- placeholder-disabled operators marked `enabledByDefault: true`.

See `operator-plugin-manifest.sample.json` for the JSON shape used by package authors.
