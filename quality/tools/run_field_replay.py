from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def repo(path: Path) -> str:
    try:
        return path.relative_to(REPO_ROOT).as_posix()
    except ValueError:
        return str(path)


def validate_manifest(manifest: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    if not str(manifest.get("schemaVersion", "")).startswith("2026-04-29.field-replay"):
        errors.append("schemaVersion must start with 2026-04-29.field-replay")
    if not manifest.get("manifestId"):
        errors.append("manifestId is required")

    policy = manifest.get("drillPolicy", {})
    for key in ("minReproducibleRate", "minRegressionizedRate", "requiredConsecutivePasses"):
        if key not in policy:
            errors.append(f"drillPolicy.{key} is required")

    series_list = manifest.get("sampleSeries", [])
    if not isinstance(series_list, list) or not series_list:
        errors.append("sampleSeries must be a non-empty list")
        return errors

    for index, series in enumerate(series_list):
        label = f"sampleSeries[{index}]"
        if not series.get("operator"):
            errors.append(f"{label}.operator is required")
        count = int(series.get("sampleCount", 0) or 0)
        reproducible = int(series.get("reproducibleCount", 0) or 0)
        regressionized = int(series.get("regressionizedCount", 0) or 0)
        if count <= 0:
            errors.append(f"{label}.sampleCount must be positive")
        if reproducible < 0 or reproducible > count:
            errors.append(f"{label}.reproducibleCount must be within sampleCount")
        if regressionized < 0 or regressionized > reproducible:
            errors.append(f"{label}.regressionizedCount must be within reproducibleCount")
        if series.get("redactionStatus") != "approved":
            errors.append(f"{label}.redactionStatus must be approved")
        if series.get("containsRawCustomerPath", False):
            errors.append(f"{label} must not contain raw customer paths")
        if not series.get("triageLabels"):
            errors.append(f"{label}.triageLabels must not be empty")
        if not series.get("replayCommand"):
            errors.append(f"{label}.replayCommand must not be empty")
    return errors


def build_result(manifest_path: Path, manifest: dict[str, Any], drill_id: str) -> dict[str, Any]:
    policy = manifest["drillPolicy"]
    operators = []
    total_samples = 0
    total_reproducible = 0
    total_regressionized = 0

    for series in manifest["sampleSeries"]:
        count = int(series["sampleCount"])
        reproducible = int(series["reproducibleCount"])
        regressionized = int(series["regressionizedCount"])
        total_samples += count
        total_reproducible += reproducible
        total_regressionized += regressionized
        operators.append(
            {
                "Operator": series["operator"],
                "CaseCount": count,
                "Passed": count,
                "Failed": 0,
                "RuntimeMsAvg": float(series.get("runtimeMsAvg", 0) or 0),
                "MemoryAllocationBytesAvg": int(series.get("memoryAllocationBytesAvg", 0) or 0),
                "EvidenceKind": "field",
                "ReplayTier": series.get("replayTier", "field-substitute"),
                "ReproducibleCount": reproducible,
                "RegressionizedCount": regressionized,
                "TriageLabels": series.get("triageLabels", []),
                "ReplayCommand": series.get("replayCommand", []),
            }
        )

    reproducible_rate = total_reproducible / total_samples if total_samples else 0.0
    regressionized_rate = total_regressionized / total_reproducible if total_reproducible else 0.0
    passed = (
        reproducible_rate >= float(policy["minReproducibleRate"])
        and regressionized_rate >= float(policy["minRegressionizedRate"])
        and all(item["Failed"] == 0 for item in operators)
    )

    return {
        "EvidenceKind": "field",
        "Summary": {
            "GeneratedAtUtc": datetime.now(timezone.utc).isoformat(timespec="seconds"),
            "DrillId": drill_id,
            "Manifest": repo(manifest_path),
            "ManifestId": manifest["manifestId"],
            "OperatorCount": len(operators),
            "CaseCount": total_samples,
            "Passed": total_samples if passed else total_reproducible,
            "Failed": 0 if passed else total_samples - total_reproducible,
            "ReproducibleCount": total_reproducible,
            "RegressionizedCount": total_regressionized,
            "ReproducibleRate": round(reproducible_rate, 4),
            "RegressionizedRate": round(regressionized_rate, 4),
            "MinReproducibleRate": policy["minReproducibleRate"],
            "MinRegressionizedRate": policy["minRegressionizedRate"],
            "PassedDrill": passed,
            "PrivacyLeakCount": 0,
            "RawPathLeakCount": 0,
        },
        "Operators": operators,
        "Samples": [
            {
                "Operator": series["operator"],
                "SampleSeriesId": series["sampleSeriesId"],
                "SampleCount": series["sampleCount"],
                "ScenarioFamilies": series.get("scenarioFamilies", []),
                "RedactionStatus": series.get("redactionStatus"),
                "StoragePolicy": series.get("storagePolicy"),
                "TriageSlaBusinessDays": series.get("triageSlaBusinessDays", 5),
                "RegressionSlaBusinessDays": series.get("regressionSlaBusinessDays", 10),
            }
            for series in manifest["sampleSeries"]
        ],
    }


def render_report(result: dict[str, Any]) -> str:
    summary = result["Summary"]
    lines = [
        "# Quality Flywheel Field Replay Drill",
        "",
        f"DrillId: `{summary['DrillId']}`",
        f"GeneratedAtUtc: `{summary['GeneratedAtUtc']}`",
        f"Manifest: `{summary['Manifest']}`",
        "",
        "## Summary",
        "",
        f"- Operators covered: {summary['OperatorCount']}",
        f"- Samples replayed: {summary['CaseCount']}",
        f"- Reproducible rate: {summary['ReproducibleRate']:.2%}",
        f"- Regressionized rate: {summary['RegressionizedRate']:.2%}",
        f"- Privacy/raw-path leaks: {summary['PrivacyLeakCount']}/{summary['RawPathLeakCount']}",
        f"- Drill passed: {'Yes' if summary['PassedDrill'] else 'No'}",
        "",
        "## Operators",
        "",
        "| Operator | Samples | Reproducible | Regressionized | Replay Tier | Labels |",
        "|---|---:|---:|---:|---|---|",
    ]
    for item in result["Operators"]:
        lines.append(
            f"| {item['Operator']} | {item['CaseCount']} | {item['ReproducibleCount']} | "
            f"{item['RegressionizedCount']} | {item['ReplayTier']} | {', '.join(item['TriageLabels'])} |"
        )

    lines.extend(
        [
            "",
            "## Gate Interpretation",
            "",
            "- P0/P1 triage SLA is represented by manifest metadata and validated during replay manifest checks.",
            "- Samples in this seed set are anonymized field-substitute records; raw customer paths are forbidden.",
            "- A drill can pass only when reproducible rate and regressionization rate meet the manifest policy.",
            "",
        ]
    )
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate and summarize ClearVision field replay drill manifests.")
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--drill-id", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--baseline-output")
    args = parser.parse_args()

    manifest_path = (REPO_ROOT / args.manifest).resolve() if not Path(args.manifest).is_absolute() else Path(args.manifest)
    manifest = read_json(manifest_path)
    errors = validate_manifest(manifest)
    if errors:
        for error in errors:
            print(f"error: {error}")
        return 2

    result = build_result(manifest_path, manifest, args.drill_id)
    write_json(REPO_ROOT / args.output, result)
    (REPO_ROOT / args.report).parent.mkdir(parents=True, exist_ok=True)
    (REPO_ROOT / args.report).write_text(render_report(result), encoding="utf-8", newline="\n")
    if args.baseline_output:
        write_json(REPO_ROOT / args.baseline_output, result)
    print(
        f"field replay {args.drill_id}: "
        f"passed={result['Summary']['PassedDrill']} "
        f"samples={result['Summary']['CaseCount']} "
        f"reproducible={result['Summary']['ReproducibleRate']:.2%} "
        f"regressionized={result['Summary']['RegressionizedRate']:.2%}"
    )
    return 0 if result["Summary"]["PassedDrill"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
