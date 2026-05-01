#!/usr/bin/env python3
"""Metrics and triage helpers for CaliperTool golden cases."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any


def width_error(expected_width: float, actual_width: float) -> float:
    return abs(float(expected_width) - float(actual_width))


def max_pair_distance_error(expected: list[float], actual: list[float]) -> float:
    if len(expected) != len(actual):
        return float("inf")
    return max((abs(float(e) - float(a)) for e, a in zip(expected, actual)), default=0.0)


def pair_count_accuracy(expected_count: int, actual_count: int) -> float:
    return 1.0 if int(expected_count) == int(actual_count) else 0.0


def expected_count_failure_correct(expected_success: bool, actual_success: bool, error_message: str | None) -> bool:
    if expected_success:
        return actual_success
    return (not actual_success) and (error_message is None or "[NoFeature]" in error_message)


def evaluate(expected: dict[str, Any], actual: dict[str, Any]) -> dict[str, object]:
    expected_success = bool(expected.get("is_success", True))
    actual_success = bool(actual.get("is_success", False))
    actual_error = actual.get("error_message")

    metrics: dict[str, object] = {
        "ExpectedSuccess": expected_success,
        "ActualSuccess": actual_success,
        "ExpectedCountFailureCorrectness": expected_count_failure_correct(
            expected_success,
            actual_success,
            str(actual_error) if actual_error is not None else None,
        ),
        "WidthErrorPx": float("inf"),
        "PairDistanceMaxErrorPx": float("inf"),
        "PairCountAccuracy": 0.0,
        "UncertaintyPxCalibration": True,
        "IsFinite": False,
    }

    if not expected_success:
        if metrics["ExpectedCountFailureCorrectness"]:
            metrics["WidthErrorPx"] = 0.0
            metrics["PairDistanceMaxErrorPx"] = 0.0
            metrics["PairCountAccuracy"] = 1.0
            metrics["IsFinite"] = True
        return metrics

    actual_width = float(actual.get("width", float("nan")))
    actual_distances = [float(value) for value in actual.get("pair_distances", [])]
    expected_distances = [float(value) for value in expected.get("pair_distances", [])]
    metrics["WidthErrorPx"] = width_error(float(expected.get("width", 0.0)), actual_width)
    metrics["PairDistanceMaxErrorPx"] = max_pair_distance_error(expected_distances, actual_distances)
    metrics["PairCountAccuracy"] = pair_count_accuracy(
        int(expected.get("pair_count", len(expected_distances))),
        int(actual.get("pair_count", 0)),
    )
    uncertainty = float(actual.get("uncertainty_px", 0.0))
    metrics["UncertaintyPxCalibration"] = math.isfinite(uncertainty) and uncertainty >= 0.0
    metrics["IsFinite"] = (
        math.isfinite(float(metrics["WidthErrorPx"]))
        and math.isfinite(float(metrics["PairDistanceMaxErrorPx"]))
        and all(math.isfinite(value) for value in actual_distances)
    )
    return metrics


def passing(metrics: dict[str, object], expected: dict[str, Any]) -> bool:
    if not metrics.get("ExpectedSuccess", True):
        return bool(metrics.get("ExpectedCountFailureCorrectness", False))
    if not metrics.get("ActualSuccess", False):
        return False
    if not metrics.get("IsFinite", False):
        return False
    if float(metrics["PairCountAccuracy"]) < 1.0:
        return False
    if not metrics.get("UncertaintyPxCalibration", False):
        return False

    width_tol = float(expected.get("width_tolerance_px", 1.0))
    pair_tol = float(expected.get("pair_distance_tolerance_px", width_tol))
    if float(metrics["WidthErrorPx"]) > width_tol:
        return False
    if float(metrics["PairDistanceMaxErrorPx"]) > pair_tol:
        return False
    return True


def summarize_baseline(baseline_path: Path) -> dict[str, Any]:
    with open(baseline_path, encoding="utf-8") as f:
        data = json.load(f)

    cases = data.get("Cases", [])
    summary: dict[str, Any] = {
        "total": len(cases),
        "passed": sum(1 for c in cases if c.get("Passed")),
        "failed": sum(1 for c in cases if not c.get("Passed")),
        "by_scenario": {},
    }

    for case in cases:
        scenario = case.get("Scenario", "unknown")
        entry = summary["by_scenario"].setdefault(
            scenario,
            {"passed": 0, "failed": 0, "cases": [], "width_errors": []},
        )
        if case.get("Passed"):
            entry["passed"] += 1
        else:
            entry["failed"] += 1
            entry["cases"].append(case)

        metrics = case.get("Metrics", {})
        width_value = metrics.get("WidthErrorPx")
        if isinstance(width_value, (int, float)) and math.isfinite(width_value):
            entry["width_errors"].append(float(width_value))

    return summary


def percentile(values: list[float], pct: float) -> float:
    if not values:
        return 0.0
    ordered = sorted(values)
    rank = (len(ordered) - 1) * pct
    low = int(math.floor(rank))
    high = int(math.ceil(rank))
    if low == high:
        return ordered[low]
    fraction = rank - low
    return ordered[low] * (1.0 - fraction) + ordered[high] * fraction


def generate_triage(baseline_path: Path, output_path: Path) -> None:
    summary = summarize_baseline(baseline_path)
    lines = [
        "# CaliperTool Failure Triage",
        "",
        f"Generated from: `{baseline_path}`",
        f"Total cases: {summary['total']}",
        f"Passed: {summary['passed']}",
        f"Failed: {summary['failed']}",
        "",
        "## Scenario Summary",
        "",
        "| Scenario | Cases | Passed | Failed | Width P95 Px | Width Max Px |",
        "|---|---:|---:|---:|---:|---:|",
    ]

    for scenario, info in sorted(summary["by_scenario"].items()):
        total = info["passed"] + info["failed"]
        errors = info["width_errors"]
        lines.append(
            f"| {scenario} | {total} | {info['passed']} | {info['failed']} | "
            f"{percentile(errors, 0.95):.4f} | {(max(errors) if errors else 0.0):.4f} |"
        )

    lines.extend(["", "## Detailed Failures", ""])
    any_failures = False
    for scenario, info in sorted(summary["by_scenario"].items()):
        if info["failed"] == 0:
            continue
        any_failures = True
        lines.append(f"### {scenario}")
        lines.append("")
        for case in info["cases"]:
            metrics = case.get("Metrics", {})
            error = case.get("ErrorMessage")
            if error:
                lines.append(f"- **{case.get('CaseId', 'unknown')}**: {error}")
            else:
                parts = ", ".join(
                    f"{key}={value:.4f}"
                    for key, value in metrics.items()
                    if isinstance(value, (int, float))
                )
                lines.append(f"- **{case.get('CaseId', 'unknown')}**: {parts}")
        lines.append("")

    if not any_failures:
        lines.append("No failing cases in this baseline.")
        lines.append("")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Triage written to {output_path}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", type=Path, default=Path("quality/evals/reports/CaliperTool_baseline.json"))
    parser.add_argument("--output", type=Path, default=Path("quality/triage/CaliperTool_failure_triage.md"))
    args = parser.parse_args()
    generate_triage(args.baseline, args.output)


if __name__ == "__main__":
    main()
