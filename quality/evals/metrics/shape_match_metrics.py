#!/usr/bin/env python3
"""Metrics for GradientShapeMatch golden cases.

Provides offline evaluation helpers that mirror the .NET runner metrics
so Python-based smoke tests and triage can reuse the same logic.
"""

from __future__ import annotations

import json
import math
from pathlib import Path
from typing import Any


def position_error(exp_x: float, exp_y: float, act_x: float, act_y: float) -> float:
    return math.hypot(exp_x - act_x, exp_y - act_y)


def angle_error(exp_angle: float, act_angle: float) -> float:
    diff = abs(exp_angle - act_angle)
    while diff > 180:
        diff = 360 - diff
    return diff


def is_match_correct(expected: bool, actual: bool) -> bool:
    return expected == actual


def evaluate(
    expected: dict[str, Any],
    actual_is_match: bool,
    actual_position: tuple[float, float] | None,
    actual_angle: float | None,
    actual_score: float,
) -> dict[str, object]:
    metrics: dict[str, object] = {
        "PositionErrorPx": float("inf"),
        "AngleErrorDeg": float("inf"),
        "IsMatchCorrect": False,
        "NoMatchAllowed": bool(expected.get("allow_no_match", False)),
        "AngleChecked": not bool(expected.get("angle_optional", False)),
        "ScoreValue": actual_score,
    }

    expected_is_match = expected.get("is_match", True)
    allow_no_match = bool(expected.get("allow_no_match", False))
    no_match_accepted = expected_is_match and allow_no_match and not actual_is_match
    metrics["IsMatchCorrect"] = is_match_correct(expected_is_match, actual_is_match) or no_match_accepted

    if expected_is_match and actual_is_match:
        exp_pos = expected.get("position", {})
        if actual_position is not None:
            metrics["PositionErrorPx"] = position_error(
                exp_pos.get("x", 0), exp_pos.get("y", 0),
                actual_position[0], actual_position[1]
            )
        if actual_angle is not None:
            metrics["AngleErrorDeg"] = angle_error(expected.get("angle", 0), actual_angle)
    elif no_match_accepted:
        metrics["PositionErrorPx"] = 0.0
        metrics["AngleErrorDeg"] = 0.0
        metrics["AngleChecked"] = False
    elif not expected_is_match and not actual_is_match:
        metrics["PositionErrorPx"] = 0.0
        metrics["AngleErrorDeg"] = 0.0

    return metrics


def passing(metrics: dict[str, object], scenario: str) -> bool:
    if not metrics.get("IsMatchCorrect", False):
        return False

    position_error = float(metrics["PositionErrorPx"])
    angle_error = float(metrics["AngleErrorDeg"])

    tolerances = {
        "translation": (3.0, 2.0),
        "roi_search": (3.0, 2.0),
        "rotation_small": (3.0, 20.0),
        "rotation_large": (5.0, 20.0),
        "blurred_edge": (5.0, 30.0),
        "low_contrast": (5.0, 45.0),
        "partial_occlusion": (10.0, 30.0),
        "strong_background": (5.0, 30.0),
        "low_feature": (0.0, 0.0),
    }

    max_pos, max_ang = tolerances.get(scenario, (3.0, 5.0))
    if position_error > max_pos:
        return False
    if metrics.get("AngleChecked", True) and angle_error > max_ang:
        return False
    return True


def summarize_baseline(baseline_path: Path) -> dict[str, Any]:
    with open(baseline_path) as f:
        data = json.load(f)

    cases = data.get("Cases", [])
    summary: dict[str, Any] = {
        "total": len(cases),
        "passed": sum(1 for c in cases if c.get("Passed")),
        "failed": sum(1 for c in cases if not c.get("Passed")),
        "by_scenario": {},
    }

    for c in cases:
        s = c.get("Scenario", "unknown")
        if s not in summary["by_scenario"]:
            summary["by_scenario"][s] = {"passed": 0, "failed": 0, "cases": []}
        entry = summary["by_scenario"][s]
        if c.get("Passed"):
            entry["passed"] += 1
        else:
            entry["failed"] += 1
            entry["cases"].append(c)

    return summary


def generate_triage(baseline_path: Path, output_path: Path) -> None:
    summary = summarize_baseline(baseline_path)
    lines = [
        "# GradientShapeMatch Failure Triage",
        "",
        f"Generated from: `{baseline_path}`",
        f"Total cases: {summary['total']}",
        f"Passed: {summary['passed']}",
        f"Failed: {summary['failed']}",
        "",
        "## Failure Summary by Scenario",
        "",
        "| Scenario | Cases | Passed | Failed |",
        "|---|---:|---:|---:|",
    ]

    for scenario, info in sorted(summary["by_scenario"].items()):
        total = info["passed"] + info["failed"]
        lines.append(f"| {scenario} | {total} | {info['passed']} | {info['failed']} |")

    lines.extend([
        "",
        "## Detailed Failures",
        "",
    ])

    for scenario, info in sorted(summary["by_scenario"].items()):
        if info["failed"] == 0:
            continue
        lines.append(f"### {scenario}")
        lines.append("")
        for c in info["cases"]:
            cid = c.get("CaseId", "unknown")
            metrics = c.get("Metrics", {})
            err = c.get("ErrorMessage")
            if err:
                lines.append(f"- **{cid}**: {err}")
            else:
                parts = ", ".join(f"{k}={v:.2f}" for k, v in metrics.items() if isinstance(v, (int, float)))
                lines.append(f"- **{cid}**: {parts}")
        lines.append("")

    output_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Triage written to {output_path}")


def main() -> None:
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", type=Path, default=Path("quality/evals/reports/GradientShapeMatch_baseline.json"))
    parser.add_argument("--output", type=Path, default=Path("quality/triage/GradientShapeMatch_failure_triage.md"))
    args = parser.parse_args()
    generate_triage(args.baseline, args.output)


if __name__ == "__main__":
    main()
