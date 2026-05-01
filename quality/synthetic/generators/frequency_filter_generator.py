#!/usr/bin/env python3
"""Generate synthetic golden cases for the FrequencyFilter operator.

Each case writes one folder:
    <output>/FrequencyFilter/<case_id>/input.json
    <output>/FrequencyFilter/<case_id>/expected.json
    <output>/FrequencyFilter/<case_id>/input_image.png   (for image-spectrum cases)
"""

from __future__ import annotations

import argparse
import json
import math
import random
from pathlib import Path
from typing import Any

import cv2
import numpy as np

OPERATOR = "FrequencyFilter"

SCENARIOS = (
    "lowpass_two_tone",
    "highpass_two_tone",
    "bandpass_multi_tone",
    "bandstop_notch",
    "cutoff_swap",
    "cutoff_clamp",
    "order_slope",
    "complex_spectrum",
    "image_2d",
)

SIGNAL_LENGTHS = (64, 128, 256, 512)
MIN_CUTOFF = 1e-6
MAX_NORMALIZED_CUTOFF = 0.5


def normalize_cutoff(cutoff: float) -> float:
    return min(max(cutoff, MIN_CUTOFF), MAX_NORMALIZED_CUTOFF)


def signed_frequency(index: int, sample_count: int) -> float:
    return index / sample_count if index <= sample_count // 2 else (index - sample_count) / sample_count


def butterworth_lowpass(frequency: float, cutoff: float, order: int) -> float:
    safe_cutoff = normalize_cutoff(cutoff)
    if frequency <= 0.0:
        return 1.0
    return 1.0 / (1.0 + (frequency / safe_cutoff) ** (2 * order))


def butterworth_highpass(frequency: float, cutoff: float, order: int) -> float:
    safe_cutoff = normalize_cutoff(cutoff)
    if frequency <= 0.0:
        return 0.0
    ratio = (frequency / safe_cutoff) ** (2 * order)
    return ratio / (1.0 + ratio)


def evaluate_filter(filter_type: str, frequency: float, cutoff_low: float, cutoff_high: float, order: int) -> float:
    band_low = min(normalize_cutoff(cutoff_low), normalize_cutoff(cutoff_high))
    band_high = max(normalize_cutoff(cutoff_low), normalize_cutoff(cutoff_high))
    kind = filter_type.lower()

    if kind in ("lowpass", "low"):
        return butterworth_lowpass(frequency, band_low, order)
    if kind in ("highpass", "high"):
        return butterworth_highpass(frequency, band_low, order)
    if kind in ("bandpass", "band"):
        return butterworth_highpass(frequency, band_low, order) * butterworth_lowpass(frequency, band_high, order)
    if kind in ("bandstop", "notch"):
        return 1.0 - (butterworth_highpass(frequency, band_low, order) * butterworth_lowpass(frequency, band_high, order))
    return 1.0


def create_mask_1d(filter_type: str, sample_count: int, cutoff_low: float, cutoff_high: float, order: int) -> np.ndarray:
    mask = np.zeros(sample_count, dtype=np.float64)
    for i in range(sample_count):
        mask[i] = evaluate_filter(filter_type, abs(signed_frequency(i, sample_count)), cutoff_low, cutoff_high, order)
    return mask


def rng_array(rng: random.Random, length: int, scale: float = 1.0) -> np.ndarray:
    return np.array([rng.gauss(0.0, scale) for _ in range(length)], dtype=np.float64)


def tone(length: int, frequency: int, amplitude: float, phase: float = 0.0) -> np.ndarray:
    t = np.arange(length)
    return amplitude * np.sin(2.0 * math.pi * frequency * t / length + phase)


def build_signal_spectrum(
    scenario: str,
    length: int,
    rng: random.Random,
) -> tuple[np.ndarray, dict[str, Any], dict[str, Any]]:
    low_bin = max(2, length // 32)
    mid_bin = max(4, length // 8)
    high_bin = max(8, length // 4)
    meta: dict[str, Any] = {"bins": {"low": low_bin, "mid": mid_bin, "high": high_bin}}

    if scenario == "lowpass_two_tone":
        signal = tone(length, low_bin, 2.0) + tone(length, high_bin, 1.0, phase=0.4)
        params = {"filter_type": "lowpass", "cutoff_low": 0.08, "cutoff_high": 0.35, "order": 4}
        meta.update({"pass_bin": low_bin, "stop_bin": high_bin, "conjugate_symmetric": True})

    elif scenario == "highpass_two_tone":
        signal = tone(length, low_bin, 2.5) + tone(length, high_bin, 1.25, phase=0.2)
        params = {"filter_type": "highpass", "cutoff_low": 0.12, "cutoff_high": 0.35, "order": 4}
        meta.update({"pass_bin": high_bin, "stop_bin": low_bin, "conjugate_symmetric": True})

    elif scenario == "bandpass_multi_tone":
        signal = (
            tone(length, low_bin, 2.0)
            + tone(length, mid_bin, 3.0, phase=0.5)
            + tone(length, high_bin, 1.5, phase=0.1)
        )
        params = {"filter_type": "bandpass", "cutoff_low": 0.08, "cutoff_high": 0.18, "order": 4}
        meta.update({"pass_bin": mid_bin, "stop_bin": high_bin, "conjugate_symmetric": True})

    elif scenario == "bandstop_notch":
        signal = (
            tone(length, low_bin, 1.5)
            + tone(length, mid_bin, 3.0, phase=0.3)
            + tone(length, high_bin, 1.0, phase=0.8)
        )
        params = {"filter_type": "bandstop", "cutoff_low": 0.08, "cutoff_high": 0.18, "order": 4}
        meta.update({"pass_bin": low_bin, "stop_bin": mid_bin, "conjugate_symmetric": True})

    elif scenario == "cutoff_swap":
        signal = (
            tone(length, low_bin, 1.0)
            + tone(length, mid_bin, 2.0, phase=0.5)
            + tone(length, high_bin, 1.0, phase=0.7)
        )
        params = {"filter_type": "bandpass", "cutoff_low": 0.22, "cutoff_high": 0.08, "order": 3}
        meta.update({"pass_bin": mid_bin, "stop_bin": high_bin, "conjugate_symmetric": True})

    elif scenario == "cutoff_clamp":
        signal = tone(length, low_bin, 1.0) + tone(length, high_bin, 2.0, phase=0.9)
        params = {"filter_type": "highpass", "cutoff_low": 0.9, "cutoff_high": -0.2, "order": 2}
        meta.update({"pass_bin": high_bin, "stop_bin": low_bin, "conjugate_symmetric": True})

    elif scenario == "order_slope":
        signal = tone(length, low_bin, 1.0) + tone(length, mid_bin, 1.0) + tone(length, high_bin, 1.0)
        params = {
            "filter_type": "lowpass",
            "cutoff_low": 0.10,
            "cutoff_high": 0.30,
            "order": rng.choice([1, 2, 4, 8]),
        }
        meta.update({"pass_bin": low_bin, "stop_bin": high_bin, "conjugate_symmetric": True})

    elif scenario == "complex_spectrum":
        real = rng_array(rng, length, scale=2.0)
        imaginary = rng_array(rng, length, scale=1.25)
        spectrum = real + 1j * imaginary
        params = {"filter_type": rng.choice(["low", "high", "band", "notch"]), "cutoff_low": 0.10, "cutoff_high": 0.24, "order": 3}
        meta.update({"pass_bin": mid_bin, "stop_bin": high_bin, "conjugate_symmetric": False})
        return spectrum, params, meta

    else:
        raise ValueError(f"Unsupported signal scenario: {scenario}")

    return np.fft.fft(signal), params, meta


def image_case(index: int, rng: random.Random) -> tuple[np.ndarray, dict[str, Any], dict[str, Any]]:
    size = 64 + (index % 3) * 16
    image = np.full((size, size), 100, dtype=np.uint8)
    stripe_freq = rng.randint(2, 7)
    for y in range(size):
        image[y, :] = np.clip(100 + int(70 * math.sin(2.0 * math.pi * stripe_freq * y / size)), 0, 255)
    cv2.circle(image, (size // 2, size // 2), size // 5, 220, thickness=-1)
    cv2.line(image, (0, size - 1), (size - 1, 0), 35, thickness=2)
    params = {"filter_type": "lowpass", "cutoff_low": 0.16, "cutoff_high": 0.35, "order": 4}
    meta = {"image_shape": [size, size], "stripe_frequency": stripe_freq}
    return image, params, meta


def round_list(values: np.ndarray, digits: int = 10) -> list[float]:
    return [round(float(value), digits) for value in values]


def build_expected(spectrum: np.ndarray, params: dict[str, Any], meta: dict[str, Any]) -> dict[str, Any]:
    mask = create_mask_1d(
        params["filter_type"],
        len(spectrum),
        params["cutoff_low"],
        params["cutoff_high"],
        params["order"],
    )
    filtered = spectrum * mask
    reconstructed = np.fft.ifft(filtered)
    return {
        "signal_length": int(len(spectrum)),
        "mask": round_list(mask),
        "filtered_spectrum": {
            "real": round_list(filtered.real),
            "imaginary": round_list(filtered.imag),
        },
        "reconstructed": {
            "real": round_list(reconstructed.real),
            "imaginary": round_list(reconstructed.imag),
        },
        "energy_before": round(float(np.sum(np.abs(spectrum) ** 2)), 8),
        "energy_after": round(float(np.sum(np.abs(filtered) ** 2)), 8),
        "pass_bin": meta.get("pass_bin", 0),
        "stop_bin": meta.get("stop_bin", 0),
        "conjugate_symmetric": bool(meta.get("conjugate_symmetric", False)),
        "is_finite": bool(np.isfinite(mask).all() and np.isfinite(filtered.real).all() and np.isfinite(filtered.imag).all()),
    }


def build_case(scenario: str, index: int, rng: random.Random) -> dict[str, Any]:
    case_id = f"{OPERATOR}_{scenario}_{index:04d}"
    length = SIGNAL_LENGTHS[index % len(SIGNAL_LENGTHS)]

    if scenario == "image_2d":
        image, params, meta = image_case(index, rng)
        return {
            "version": 1,
            "case_id": case_id,
            "task": "frequency_filter",
            "operator": OPERATOR,
            "scenario": scenario,
            "inputs": {
                "input_type": "image_source",
                "image": "input_image.png",
                **params,
            },
            "expected": {
                "image_shape": meta["image_shape"],
                "mask_min": 0.0,
                "mask_max": 1.0,
                "is_finite": True,
            },
            "meta": meta,
            "metrics": [
                "MaskMaxError",
                "MaskRangeCorrect",
                "IsFinite",
                "OutputShapeCorrect",
                "RuntimeMs",
                "MemoryAllocation",
            ],
            "image": image,
        }

    spectrum, params, meta = build_signal_spectrum(scenario, length, rng)
    expected = build_expected(spectrum, params, meta)
    return {
        "version": 1,
        "case_id": case_id,
        "task": "frequency_filter",
        "operator": OPERATOR,
        "scenario": scenario,
        "inputs": {
            "input_type": "complex_array",
            "spectrum": {
                "real": round_list(spectrum.real),
                "imaginary": round_list(spectrum.imag),
            },
            **params,
        },
        "expected": expected,
        "meta": meta,
        "metrics": [
            "MaskLengthCorrect",
            "MaskMaxError",
            "MaskRangeCorrect",
            "FilteredSpectrumMaxError",
            "FilteredSpectrumRmse",
            "ReconstructionRmse",
            "ImaginaryRmse",
            "EnergyError",
            "ConjugateSymmetryError",
            "PassStopRatioCorrect",
            "IsFinite",
            "OutputShapeCorrect",
            "RuntimeMs",
            "MemoryAllocation",
        ],
    }


def write_case(output_root: Path, case: dict[str, Any]) -> None:
    case_dir = output_root / OPERATOR / str(case["case_id"])
    case_dir.mkdir(parents=True, exist_ok=True)

    input_payload = {
        key: case[key]
        for key in (
            "version",
            "case_id",
            "task",
            "operator",
            "scenario",
            "inputs",
            "metrics",
        )
    }
    if "meta" in case:
        input_payload["meta"] = case["meta"]

    expected_payload = {
        "case_id": case["case_id"],
        "task": case["task"],
        "operator": case["operator"],
        "expected": case["expected"],
    }

    (case_dir / "input.json").write_text(json.dumps(input_payload, indent=2), encoding="utf-8")
    (case_dir / "expected.json").write_text(json.dumps(expected_payload, indent=2), encoding="utf-8")

    if "image" in case:
        cv2.imwrite(str(case_dir / "input_image.png"), case["image"])


def generate(output_root: Path, count: int, seed: int) -> list[dict[str, Any]]:
    rng = random.Random(seed)
    cases: list[dict[str, Any]] = []
    per_scenario = max(1, count // len(SCENARIOS))

    for scenario in SCENARIOS:
        for i in range(per_scenario):
            case = build_case(scenario, i, rng)
            write_case(output_root, case)
            cases.append(case)

    manifest = {
        "version": 1,
        "seed": seed,
        "case_count": len(cases),
        "operator": OPERATOR,
        "scenarios": list(SCENARIOS),
        "cases": [
            {
                "case_id": case["case_id"],
                "scenario": case["scenario"],
            }
            for case in cases
        ],
    }
    (output_root / "manifest_frequency_filter.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return cases


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate FrequencyFilter golden cases.")
    parser.add_argument("--output", type=Path, default=Path("quality/synthetic/cases/fft"))
    parser.add_argument("--count", type=int, default=120, help="Total cases; rounded down by scenario.")
    parser.add_argument("--seed", type=int, default=4409)
    args = parser.parse_args()

    cases = generate(args.output, args.count, args.seed)
    print(f"Generated {len(cases)} FrequencyFilter cases under {args.output}")


if __name__ == "__main__":
    main()
