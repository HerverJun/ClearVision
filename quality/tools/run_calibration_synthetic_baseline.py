#!/usr/bin/env python3
"""Run calibration synthetic baseline and write matrix-compatible evidence."""

from __future__ import annotations

import argparse
import json
import shutil
import sys
import time
import tracemalloc
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from quality.evals.metrics.calibration_metrics import evaluate_case  # noqa: E402
from quality.synthetic.generators.calibration_generator import generate_cases, write_cases  # noqa: E402


def run_case(case: dict[str, Any]) -> dict[str, Any]:
    tracemalloc.start()
    start = time.perf_counter()
    metrics = evaluate_case(case)
    current, peak = tracemalloc.get_traced_memory()
    tracemalloc.stop()
    runtime_ms = (time.perf_counter() - start) * 1000.0
    passed = bool(metrics.pop("Passed"))
    return {
        "CaseId": case["case_id"],
        "Operator": case["operator"],
        "Scenario": case["scenario"],
        "Passed": passed,
        "RuntimeMs": round(runtime_ms, 3),
        "MemoryAllocationBytes": int(max(current, peak)),
        "ErrorMessage": None if passed else "Calibration metric threshold mismatch",
        "Metrics": metrics,
    }


def summarize(results: list[dict[str, Any]], cases_root: Path) -> dict[str, Any]:
    operators: list[dict[str, Any]] = []
    scenarios: list[dict[str, Any]] = []
    by_operator: dict[str, list[dict[str, Any]]] = defaultdict(list)
    by_scenario: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for result in results:
        by_operator[result["Operator"]].append(result)
        by_scenario[result["Scenario"]].append(result)

    for operator, items in sorted(by_operator.items()):
        operators.append(summary_row("Operator", operator, items))

    for scenario, items in sorted(by_scenario.items()):
        scenarios.append(summary_row("Scenario", scenario, items))

    return {
        "Summary": {
            "GeneratedAtUtc": datetime.now(timezone.utc).isoformat(),
            "CasesRoot": str(cases_root).replace("\\", "/"),
            "CaseCount": len(results),
            "Passed": sum(1 for item in results if item["Passed"]),
            "Failed": sum(1 for item in results if not item["Passed"]),
            "MemoryAllocationBytesAvgSum": sum(item["MemoryAllocationBytesAvg"] for item in operators),
        },
        "Operators": operators,
        "Scenarios": scenarios,
        "Cases": results,
    }


def summary_row(key: str, value: str, items: list[dict[str, Any]]) -> dict[str, Any]:
    return {
        key: value,
        "CaseCount": len(items),
        "Passed": sum(1 for item in items if item["Passed"]),
        "Failed": sum(1 for item in items if not item["Passed"]),
        "RuntimeMsAvg": round(sum(float(item["RuntimeMs"]) for item in items) / len(items), 3),
        "RuntimeMsMax": round(max(float(item["RuntimeMs"]) for item in items), 3),
        "MemoryAllocationBytesAvg": round(sum(int(item["MemoryAllocationBytes"]) for item in items) / len(items)),
    }


def markdown_report(result: dict[str, Any]) -> str:
    summary = result["Summary"]
    lines = [
        "# Calibration Synthetic Baseline",
        "",
        f"GeneratedAtUtc: `{summary['GeneratedAtUtc']}`",
        f"CasesRoot: `{summary['CasesRoot']}`",
        "",
        "## Summary",
        "",
        f"- Cases: {summary['CaseCount']}",
        f"- Passed: {summary['Passed']}",
        f"- Failed: {summary['Failed']}",
        "",
        "## Operators",
        "",
        "| Operator | Cases | Passed | Failed | RuntimeMsAvg | MemoryAllocationBytesAvg |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for item in result["Operators"]:
        lines.append(
            f"| {item['Operator']} | {item['CaseCount']} | {item['Passed']} | {item['Failed']} | "
            f"{item['RuntimeMsAvg']} | {item['MemoryAllocationBytesAvg']} |"
        )
    lines.extend([
        "",
        "## Scenarios",
        "",
        "| Scenario | Cases | Passed | Failed | RuntimeMsAvg |",
        "|---|---:|---:|---:|---:|",
    ])
    for item in result["Scenarios"]:
        lines.append(
            f"| {item['Scenario']} | {item['CaseCount']} | {item['Passed']} | "
            f"{item['Failed']} | {item['RuntimeMsAvg']} |"
        )
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description="Run calibration synthetic baseline.")
    parser.add_argument("--cases-root", type=Path, default=REPO_ROOT / "quality" / "synthetic" / "cases" / "calibration")
    parser.add_argument("--output", type=Path, default=REPO_ROOT / "quality" / "evals" / "reports" / "CalibrationSynthetic_baseline.json")
    parser.add_argument("--report", type=Path, default=REPO_ROOT / "quality" / "evals" / "reports" / "CalibrationSynthetic_baseline.md")
    parser.add_argument("--per-operator", type=int, default=24)
    parser.add_argument("--seed", type=int, default=4242)
    parser.add_argument("--keep-cases", action="store_true")
    args = parser.parse_args()

    cases = generate_cases(args.per_operator, args.seed)
    if args.cases_root.exists():
        shutil.rmtree(args.cases_root)
    write_cases(cases, args.cases_root)

    results = [run_case(case) for case in cases]
    result = summarize(results, args.cases_root.relative_to(REPO_ROOT))

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(markdown_report(result), encoding="utf-8")

    if not args.keep_cases:
        shutil.rmtree(args.cases_root, ignore_errors=True)

    print(
        f"Calibration synthetic baseline complete: "
        f"{result['Summary']['Passed']}/{result['Summary']['CaseCount']} passed, "
        f"failed={result['Summary']['Failed']}, output={args.output}"
    )
    return 0 if result["Summary"]["Failed"] == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
