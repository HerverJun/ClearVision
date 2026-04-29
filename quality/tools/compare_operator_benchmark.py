#!/usr/bin/env python3
"""Compare two OperatorPerformanceBenchmarkRunner JSON reports."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


def load_report(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def case_index(report: dict[str, Any]) -> dict[str, dict[str, Any]]:
    return {case["caseId"]: case for case in report.get("cases", [])}


def pct_delta(previous: float, current: float) -> str:
    if previous == 0:
        return "n/a"

    return f"{((current - previous) / previous) * 100:+.2f}%"


def create_markdown(baseline: dict[str, Any] | None, current: dict[str, Any], baseline_path: Path | None, current_path: Path) -> str:
    current_summary = current["summary"]
    lines = [
        "# Operator Performance Trend Report",
        "",
        f"- Current report: `{current_path.as_posix()}`",
        f"- Current generated UTC: `{current_summary.get('generatedAtUtc', '')}`",
        f"- Current mode: `{current_summary.get('mode', '')}`",
        f"- Current cases passed: {current_summary.get('passed')}/{current_summary.get('caseCount')}",
    ]

    if baseline is None:
        lines.extend(
            [
                "- Baseline report: not provided",
                "",
                "No baseline was supplied, so this report records the current run as the first comparable point.",
            ]
        )
    else:
        baseline_summary = baseline["summary"]
        lines.extend(
            [
                f"- Baseline report: `{baseline_path.as_posix() if baseline_path else ''}`",
                f"- Baseline generated UTC: `{baseline_summary.get('generatedAtUtc', '')}`",
                f"- Baseline cases passed: {baseline_summary.get('passed')}/{baseline_summary.get('caseCount')}",
            ]
        )

    lines.extend(
        [
            "",
            "## Case Delta",
            "",
            "| Case | Baseline mean ms | Current mean ms | Delta | Baseline alloc/iter | Current alloc/iter | Passed |",
            "| --- | ---: | ---: | ---: | ---: | ---: | --- |",
        ]
    )

    current_cases = case_index(current)
    baseline_cases = case_index(baseline) if baseline is not None else {}
    for case_id in sorted(current_cases):
        current_case = current_cases[case_id]
        baseline_case = baseline_cases.get(case_id)
        baseline_mean = float(baseline_case["meanRuntimeMs"]) if baseline_case else 0.0
        current_mean = float(current_case["meanRuntimeMs"])
        baseline_alloc = int(baseline_case["allocatedBytesPerIteration"]) if baseline_case else 0
        current_alloc = int(current_case["allocatedBytesPerIteration"])
        lines.append(
            "| "
            + " | ".join(
                [
                    case_id,
                    f"{baseline_mean:.3f}" if baseline_case else "n/a",
                    f"{current_mean:.3f}",
                    pct_delta(baseline_mean, current_mean) if baseline_case else "n/a",
                    str(baseline_alloc) if baseline_case else "n/a",
                    str(current_alloc),
                    str(bool(current_case.get("passed"))),
                ]
            )
            + " |"
        )

    lines.extend(
        [
            "",
            "## Gate Notes",
            "",
            "- `smoke` mode is suitable for CI signal and trend drift detection.",
            "- Release gates should compare against a pinned baseline from the same machine profile.",
            "- A single local run is not a release conclusion by itself.",
            "",
        ]
    )
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--current", required=True, type=Path)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    current = load_report(args.current)
    baseline = load_report(args.baseline) if args.baseline else None

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(create_markdown(baseline, current, args.baseline, args.current), encoding="utf-8")
    print(f"wrote {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
