#!/usr/bin/env python3
"""Metrics and triage helpers for TemplateMatching golden cases."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any


def distance(a: dict[str, float], b: dict[str, float]) -> float:
    return math.hypot(float(a.get("x", 0.0)) - float(b.get("x", 0.0)), float(a.get("y", 0.0)) - float(b.get("y", 0.0)))


def max_assignment_error(expected: list[dict[str, float]], actual: list[dict[str, float]]) -> float:
    if len(actual) < len(expected):
        return float("inf")
    remaining = actual[:]
    max_error = 0.0
    for exp in expected:
        if not remaining:
            return float("inf")
        index, error = min(enumerate(distance(exp, act) for act in remaining), key=lambda item: item[1])
        max_error = max(max_error, error)
        remaining.pop(index)
    return max_error


def score_contract_correct(method: str, is_match: bool, score: float, normalized_score: float, raw_response: float) -> bool:
    if not is_match:
        return score == 0.0 and normalized_score == 0.0 and raw_response == 0.0
    if not all(math.isfinite(v) for v in (score, normalized_score, raw_response)):
        return False
    if normalized_score < -1e-6 or normalized_score > 1.0 + 1e-6:
        return False
    if method in {"SqDiff", "SqDiffNormed"} and abs(score - normalized_score) > 1e-6:
        return False
    if method == "SqDiffNormed" and not (0.0 <= raw_response <= 1.0 + 1e-6):
        return False
    if method == "SqDiff" and raw_response < -1e-6:
        return False
    return True


def evaluate(expected: dict[str, Any], actual: dict[str, Any]) -> dict[str, object]:
    expected_match = bool(expected.get("is_match", True))
    actual_match = bool(actual.get("is_match", False))
    method = str(expected.get("score_contract", expected.get("method", "CCoeffNormed"))).split(":")[0]
    score = float(actual.get("score", 0.0))
    normalized = float(actual.get("normalized_score", 0.0))
    raw = float(actual.get("raw_response", 0.0))
    actual_positions = actual.get("positions", [])
    expected_positions = expected.get("positions", [])

    metrics: dict[str, object] = {
        "IsMatchCorrect": expected_match == actual_match,
        "MatchCountCorrect": int(expected.get("match_count", 0)) == int(actual.get("match_count", 0)),
        "PositionErrorPx": 0.0,
        "ScoreContractCorrect": score_contract_correct(method, actual_match, score, normalized, raw),
        "NormalizedScoreInRange": 0.0 <= normalized <= 1.0,
        "MinScoreSatisfied": normalized >= float(expected.get("min_normalized_score", 0.0)) if expected_match else True,
        "ExpectedFailureCorrect": True,
        "NmsDistinct": bool(actual.get("nms_distinct", True)),
        "IsFinite": all(math.isfinite(v) for v in (score, normalized, raw)),
    }

    if expected_match:
        metrics["PositionErrorPx"] = max_assignment_error(expected_positions, actual_positions)
    else:
        reason = str(expected.get("expected_failure_contains", ""))
        failure_reason = str(actual.get("failure_reason", ""))
        metrics["ExpectedFailureCorrect"] = (not actual_match) and (not reason or reason.lower() in failure_reason.lower())

    return metrics


def passing(metrics: dict[str, object], expected: dict[str, Any]) -> bool:
    if not metrics.get("IsMatchCorrect", False):
        return False
    if not metrics.get("MatchCountCorrect", False):
        return False
    if not metrics.get("ScoreContractCorrect", False):
        return False
    if not metrics.get("NormalizedScoreInRange", False):
        return False
    if not metrics.get("ExpectedFailureCorrect", False):
        return False
    if not metrics.get("NmsDistinct", False):
        return False
    if bool(expected.get("is_match", True)):
        if not metrics.get("MinScoreSatisfied", False):
            return False
        if float(metrics["PositionErrorPx"]) > float(expected.get("position_tolerance_px", 1.0)):
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
            {"passed": 0, "failed": 0, "cases": [], "position_errors": []},
        )
        if case.get("Passed"):
            entry["passed"] += 1
        else:
            entry["failed"] += 1
            entry["cases"].append(case)

        value = case.get("Metrics", {}).get("PositionErrorPx")
        if isinstance(value, (int, float)) and math.isfinite(value):
            entry["position_errors"].append(float(value))
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
    frac = rank - low
    return ordered[low] * (1.0 - frac) + ordered[high] * frac


def generate_triage(baseline_path: Path, output_path: Path) -> None:
    summary = summarize_baseline(baseline_path)
    lines = [
        "# TemplateMatching Failure Triage",
        "",
        f"Generated from: `{baseline_path}`",
        f"Total cases: {summary['total']}",
        f"Passed: {summary['passed']}",
        f"Failed: {summary['failed']}",
        "",
        "## Scenario Summary",
        "",
        "| Scenario | Cases | Passed | Failed | Position P95 Px | Position Max Px |",
        "|---|---:|---:|---:|---:|---:|",
    ]

    for scenario, info in sorted(summary["by_scenario"].items()):
        total = info["passed"] + info["failed"]
        errors = info["position_errors"]
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


def load_cases_with_inputs(baseline_path: Path) -> list[dict[str, Any]]:
    with open(baseline_path, encoding="utf-8") as f:
        data = json.load(f)

    cases = data.get("Cases", [])
    enriched = []
    for case in cases:
        input_path = Path(case.get("InputPath", ""))
        input_payload: dict[str, Any] = {}
        if input_path.exists():
            with open(input_path, encoding="utf-8") as f:
                input_payload = json.load(f)
        enriched.append({"case": case, "input": input_payload})
    return enriched


def generate_score_contract_report(baseline_path: Path, output_path: Path) -> None:
    rows: dict[tuple[str, str], dict[str, Any]] = {}
    for item in load_cases_with_inputs(baseline_path):
        case = item["case"]
        params = item["input"].get("params", {})
        method = params.get("Method", "-")
        domain = params.get("Domain", "-")
        key = (method, domain)
        metrics = case.get("Metrics", {})
        entry = rows.setdefault(
            key,
            {
                "cases": 0,
                "passed": 0,
                "score_contract": 0,
                "norm_min": [],
                "norm_max": [],
                "raw_max": [],
            },
        )
        entry["cases"] += 1
        entry["passed"] += 1 if case.get("Passed") else 0
        entry["score_contract"] += 1 if metrics.get("ScoreContractCorrect") else 0
        norm = metrics.get("NormalizedScoreValue")
        raw = metrics.get("RawResponseValue")
        if isinstance(norm, (int, float)) and math.isfinite(norm):
            entry["norm_min"].append(float(norm))
            entry["norm_max"].append(float(norm))
        if isinstance(raw, (int, float)) and math.isfinite(raw):
            entry["raw_max"].append(abs(float(raw)))

    lines = [
        "# TemplateMatching Score Contract Report",
        "",
        f"Generated from: `{baseline_path}`",
        "",
        "| Method | Domain | Cases | Passed | Score Contract OK | Normalized Min | Normalized Max | Abs Raw Max |",
        "|---|---|---:|---:|---:|---:|---:|---:|",
    ]
    for (method, domain), info in sorted(rows.items()):
        norm_values = info["norm_min"]
        raw_values = info["raw_max"]
        lines.append(
            f"| {method} | {domain} | {info['cases']} | {info['passed']} | {info['score_contract']} | "
            f"{(min(norm_values) if norm_values else 0.0):.4f} | "
            f"{(max(norm_values) if norm_values else 0.0):.4f} | "
            f"{(max(raw_values) if raw_values else 0.0):.4f} |"
        )
    lines.append("")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Score contract report written to {output_path}")


def generate_robustness_report(baseline_path: Path, output_path: Path) -> None:
    summary = summarize_baseline(baseline_path)
    lines = [
        "# TemplateMatching Matching Robustness Report",
        "",
        f"Generated from: `{baseline_path}`",
        "",
        "| Scenario | Cases | Passed | Failed | Position P95 Px | Position Max Px |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for scenario, info in sorted(summary["by_scenario"].items()):
        total = info["passed"] + info["failed"]
        errors = info["position_errors"]
        lines.append(
            f"| {scenario} | {total} | {info['passed']} | {info['failed']} | "
            f"{percentile(errors, 0.95):.4f} | {(max(errors) if errors else 0.0):.4f} |"
        )

    lines.extend(
        [
            "",
            "## Boundary Checks",
            "",
            "- `low_texture` expects `IsMatch=false` with an insufficient-texture failure reason.",
            "- `fixed_scale_boundary` expects `IsMatch=false`, locking the fixed-scale/no-rotation limitation.",
            "- ROI and Mask scenarios verify that stronger decoys outside the allowed search area are not returned.",
            "- Multi-match scenarios verify `MaxMatches` and IoU NMS distinctness.",
            "",
        ]
    )
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Robustness report written to {output_path}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", type=Path, default=Path("quality/evals/reports/TemplateMatching_baseline.json"))
    parser.add_argument("--output", type=Path, default=Path("quality/triage/TemplateMatching_failure_triage.md"))
    parser.add_argument("--score-report", type=Path, default=Path("quality/evals/reports/TemplateMatching_score_contract.md"))
    parser.add_argument("--robustness-report", type=Path, default=Path("quality/evals/reports/TemplateMatching_matching_robustness.md"))
    args = parser.parse_args()
    generate_triage(args.baseline, args.output)
    generate_score_contract_report(args.baseline, args.score_report)
    generate_robustness_report(args.baseline, args.robustness_report)


if __name__ == "__main__":
    main()
