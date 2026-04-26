#!/usr/bin/env python3
"""Metrics and triage helpers for FFT1D / InverseFFT1D golden cases."""

from __future__ import annotations

import json
import math
from pathlib import Path
from typing import Any


def dominant_index_error(expected: int, actual: int) -> int:
    return abs(expected - actual)


def relative_error(expected: float, actual: float) -> float:
    if expected == 0.0:
        return abs(actual)
    return abs(expected - actual) / max(abs(expected), 1e-12)


def reconstruction_rmse(original: list[float], reconstructed: list[float]) -> float:
    if len(original) != len(reconstructed):
        return float("inf")
    sq = sum((a - b) ** 2 for a, b in zip(original, reconstructed))
    return math.sqrt(sq / max(len(original), 1))


def max_abs_error(expected: list[float], actual: list[float]) -> float:
    if len(expected) != len(actual):
        return float("inf")
    return max((abs(a - b) for a, b in zip(expected, actual)), default=0.0)


def signal_energy(real: list[float], imaginary: list[float] | None = None) -> float:
    if imaginary is None:
        imaginary = [0.0] * len(real)
    if len(real) != len(imaginary):
        return float("inf")
    return sum(r * r + i * i for r, i in zip(real, imaginary))


def is_finite(values: list[float]) -> bool:
    return all(math.isfinite(v) for v in values)


def evaluate(
    expected: dict[str, Any],
    actual_dominant_index: int,
    actual_dc_magnitude: float,
    actual_max_magnitude: float,
    actual_reconstruction_rmse: float,
    actual_is_finite: bool,
) -> dict[str, object]:
    return {
        "DominantIndexError": dominant_index_error(expected.get("dominant_index", 0), actual_dominant_index),
        "DcMagnitudeError": relative_error(expected.get("dc_magnitude", 0.0), actual_dc_magnitude),
        "MaxMagnitudeError": relative_error(expected.get("max_magnitude", 0.0), actual_max_magnitude),
        "ReconstructionRmse": actual_reconstruction_rmse,
        "IsFinite": actual_is_finite,
    }


def passing(metrics: dict[str, object], scenario: str) -> bool:
    if not metrics.get("IsFinite", False):
        return False

    dom_err = int(metrics["DominantIndexError"])
    dc_err = float(metrics["DcMagnitudeError"])
    max_err = float(metrics["MaxMagnitudeError"])
    rmse = float(metrics["ReconstructionRmse"])

    if dom_err > 0 and scenario not in ("random_noise", "square_wave", "impulse"):
        return False
    if dc_err > 1e-3 and scenario not in ("random_noise",):
        return False
    if max_err > 1e-3 and scenario not in ("random_noise",):
        return False
    if rmse > 1e-4:
        return False

    return True


def evaluate_inverse(
    expected_real: list[float],
    expected_imaginary: list[float],
    actual_real: list[float],
    actual_imaginary: list[float],
) -> dict[str, object]:
    length_correct = (
        len(expected_real)
        == len(expected_imaginary)
        == len(actual_real)
        == len(actual_imaginary)
    )
    actual_finite = is_finite(actual_real) and is_finite(actual_imaginary)
    expected_energy = signal_energy(expected_real, expected_imaginary)
    actual_energy = signal_energy(actual_real, actual_imaginary)

    return {
        "SignalLengthCorrect": length_correct,
        "MaxRealError": max_abs_error(expected_real, actual_real),
        "RmseReal": reconstruction_rmse(expected_real, actual_real),
        "MaxImaginaryError": max_abs_error(expected_imaginary, actual_imaginary),
        "ImaginaryMaxAbs": max((abs(v) for v in actual_imaginary), default=0.0),
        "EnergyError": relative_error(expected_energy, actual_energy),
        "IsFinite": actual_finite,
    }


def passing_inverse(metrics: dict[str, object], scenario: str) -> bool:
    if not metrics.get("IsFinite", False):
        return False
    if not metrics.get("SignalLengthCorrect", False):
        return False
    if float(metrics["MaxRealError"]) > 1e-3:
        return False
    if float(metrics["RmseReal"]) > 1e-4:
        return False
    if float(metrics["MaxImaginaryError"]) > 1e-3:
        return False
    if float(metrics["EnergyError"]) > 1e-3:
        return False
    return True


def complex_spectrum_error(
    expected_real: list[float],
    expected_imaginary: list[float],
    actual_real: list[float],
    actual_imaginary: list[float],
) -> tuple[float, float]:
    if not (
        len(expected_real)
        == len(expected_imaginary)
        == len(actual_real)
        == len(actual_imaginary)
    ):
        return float("inf"), float("inf")

    max_error = 0.0
    sq = 0.0
    for er, ei, ar, ai in zip(expected_real, expected_imaginary, actual_real, actual_imaginary):
        err = math.hypot(er - ar, ei - ai)
        max_error = max(max_error, err)
        sq += err * err
    return max_error, math.sqrt(sq / max(len(expected_real), 1))


def conjugate_symmetry_error(real: list[float], imaginary: list[float]) -> float:
    if len(real) != len(imaginary):
        return float("inf")
    n = len(real)
    max_error = 0.0
    for i in range(1, n):
        j = (n - i) % n
        max_error = max(max_error, math.hypot(real[i] - real[j], imaginary[i] + imaginary[j]))
    return max_error


def evaluate_frequency_filter(
    expected: dict[str, Any],
    actual_mask: list[float],
    actual_filtered_real: list[float],
    actual_filtered_imaginary: list[float],
) -> dict[str, object]:
    expected_mask = expected.get("mask", [])
    expected_spectrum = expected.get("filtered_spectrum", {})
    expected_real = expected_spectrum.get("real", [])
    expected_imaginary = expected_spectrum.get("imaginary", [])
    max_spectrum_error, spectrum_rmse = complex_spectrum_error(
        expected_real,
        expected_imaginary,
        actual_filtered_real,
        actual_filtered_imaginary,
    )

    actual_energy = sum(
        r * r + i * i
        for r, i in zip(actual_filtered_real, actual_filtered_imaginary)
    )
    symmetry_error = (
        conjugate_symmetry_error(actual_filtered_real, actual_filtered_imaginary)
        if expected.get("conjugate_symmetric", False)
        else 0.0
    )

    return {
        "MaskLengthCorrect": len(actual_mask) == len(expected_mask),
        "MaskMaxError": max_abs_error(expected_mask, actual_mask),
        "MaskRangeCorrect": all(-1e-9 <= v <= 1.0 + 1e-9 for v in actual_mask),
        "FilteredSpectrumMaxError": max_spectrum_error,
        "FilteredSpectrumRmse": spectrum_rmse,
        "EnergyError": relative_error(expected.get("energy_after", 0.0), actual_energy),
        "ConjugateSymmetryError": symmetry_error,
        "IsFinite": (
            is_finite(actual_mask)
            and is_finite(actual_filtered_real)
            and is_finite(actual_filtered_imaginary)
        ),
    }


def passing_frequency_filter(metrics: dict[str, object], scenario: str) -> bool:
    if not metrics.get("IsFinite", False):
        return False
    if not metrics.get("MaskLengthCorrect", False):
        return False
    if not metrics.get("MaskRangeCorrect", False):
        return False
    if float(metrics["MaskMaxError"]) > 1e-5:
        return False
    if float(metrics["FilteredSpectrumMaxError"]) > 1e-4:
        return False
    if float(metrics["FilteredSpectrumRmse"]) > 1e-5:
        return False
    if float(metrics["EnergyError"]) > 1e-4:
        return False
    if float(metrics["ConjugateSymmetryError"]) > 1e-3:
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


def operator_name_from_baseline(baseline_path: Path) -> str:
    with open(baseline_path) as f:
        data = json.load(f)
    operators = data.get("Operators", [])
    if operators:
        return str(operators[0].get("Operator", "Frequency"))
    return baseline_path.stem.replace("_baseline", "")


def generate_triage(baseline_path: Path, output_path: Path) -> None:
    summary = summarize_baseline(baseline_path)
    operator_name = operator_name_from_baseline(baseline_path)
    lines = [
        f"# {operator_name} Failure Triage",
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
                parts = ", ".join(f"{k}={v:.4f}" for k, v in metrics.items() if isinstance(v, (int, float)))
                lines.append(f"- **{cid}**: {parts}")
        lines.append("")

    output_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Triage written to {output_path}")


def main() -> None:
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", type=Path, default=Path("quality/evals/reports/FFT1D_baseline.json"))
    parser.add_argument("--output", type=Path, default=Path("quality/triage/FFT1D_failure_triage.md"))
    args = parser.parse_args()
    generate_triage(args.baseline, args.output)


if __name__ == "__main__":
    main()
