#!/usr/bin/env python3
"""Generate synthetic golden cases for FFT1D / InverseFFT1D operators.

Each case writes one folder:
    <output>/FFT1D/<case_id>/input.json
    <output>/FFT1D/<case_id>/expected.json
    <output>/FFT1D/<case_id>/input_image.png   (for image-input cases only)

The generator covers 1D signal and 2D image inputs so that both
"TransformKind" branches in the operator are exercised.
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

OPERATOR = "FFT1D"

SCENARIOS = (
    "zero_signal",
    "constant_signal",
    "single_frequency",
    "multi_frequency",
    "square_wave",
    "impulse",
    "random_noise",
    "composite",
    "image_2d",
)

SIGNAL_LENGTHS = (64, 128, 256, 512)


def generate_signal(scenario: str, length: int, rng: random.Random) -> tuple[np.ndarray, dict[str, Any]]:
    """Returns (signal_array, expected_metrics)."""
    signal = np.zeros(length, dtype=np.float64)
    meta: dict[str, Any] = {}

    if scenario == "zero_signal":
        signal[:] = 0.0
        meta["dc_magnitude"] = 0.0
        meta["max_magnitude"] = 0.0
        meta["dominant_index"] = 0
        meta["is_finite"] = True

    elif scenario == "constant_signal":
        value = rng.choice([1.0, 5.0, -3.0])
        signal[:] = value
        meta["dc_magnitude"] = abs(value) * length
        meta["max_magnitude"] = meta["dc_magnitude"]
        meta["dominant_index"] = 0
        meta["is_finite"] = True

    elif scenario == "single_frequency":
        # Pick a frequency that is an integer number of cycles in the signal
        freq = rng.randint(1, length // 4)
        amplitude = rng.uniform(1.0, 10.0)
        phase = rng.uniform(0.0, 2 * math.pi)
        t = np.arange(length)
        signal = amplitude * np.sin(2 * math.pi * freq * t / length + phase)
        meta["expected_freq"] = freq
        meta["amplitude"] = amplitude
        meta["phase"] = phase
        meta["dominant_index"] = freq
        meta["is_finite"] = True

    elif scenario == "multi_frequency":
        freqs = rng.sample(range(1, length // 4), k=rng.randint(2, 4))
        meta["expected_freqs"] = sorted(freqs)
        for f in freqs:
            amp = rng.uniform(1.0, 5.0)
            ph = rng.uniform(0.0, 2 * math.pi)
            t = np.arange(length)
            signal += amp * np.sin(2 * math.pi * f * t / length + ph)
        meta["dominant_index"] = max(freqs, key=lambda f: sum(1 for x in freqs if x == f))
        meta["is_finite"] = True

    elif scenario == "square_wave":
        period = rng.choice([4, 8, 16, 32])
        for i in range(length):
            signal[i] = 1.0 if (i % period) < (period // 2) else -1.0
        meta["period"] = period
        meta["is_finite"] = True

    elif scenario == "impulse":
        pos = rng.randint(0, length - 1)
        signal[pos] = 1.0
        meta["impulse_position"] = pos
        meta["dominant_index"] = 0  # flat spectrum
        meta["is_finite"] = True

    elif scenario == "random_noise":
        signal = rng.gauss(0, 1) + np.array([rng.gauss(0, 1) for _ in range(length)])
        meta["is_finite"] = True

    elif scenario == "composite":
        # Mix of DC + low freq + high freq + small noise
        dc = rng.uniform(-5.0, 5.0)
        low_freq = rng.randint(1, length // 8)
        high_freq = rng.randint(length // 8, length // 2 - 1)
        t = np.arange(length)
        signal = (
            dc
            + 3.0 * np.sin(2 * math.pi * low_freq * t / length)
            + 1.5 * np.sin(2 * math.pi * high_freq * t / length)
            + 0.1 * np.random.randn(length)
        )
        meta["dc"] = dc
        meta["low_freq"] = low_freq
        meta["high_freq"] = high_freq
        meta["is_finite"] = True

    elif scenario == "image_2d":
        # Create a small synthetic grayscale image with horizontal stripes
        h, w = 64, 64
        img = np.full((h, w), 128, dtype=np.uint8)
        stripe_freq = rng.randint(2, 8)
        for y in range(h):
            intensity = 128 + int(100 * math.sin(2 * math.pi * stripe_freq * y / h))
            img[y, :] = np.clip(intensity, 0, 255)
        return img, {"is_finite": True, "image_shape": [h, w]}

    return signal, meta


def compute_expected_fft(signal: np.ndarray) -> dict[str, Any]:
    """Use numpy FFT to compute ground-truth for verification."""
    n = len(signal)
    spectrum = np.fft.fft(signal)
    magnitudes = np.abs(spectrum)
    phases = np.angle(spectrum)

    dominant_idx = int(np.argmax(magnitudes))
    dc_mag = float(magnitudes[0])
    max_mag = float(magnitudes[dominant_idx])

    # Round-trip reconstruction
    reconstructed = np.fft.ifft(spectrum).real
    rmse = float(np.sqrt(np.mean((signal - reconstructed) ** 2)))

    return {
        "dominant_index": dominant_idx,
        "dc_magnitude": round(dc_mag, 6),
        "max_magnitude": round(max_mag, 6),
        "reconstruction_rmse": round(rmse, 10),
        "is_finite": bool(np.isfinite(magnitudes).all() and np.isfinite(phases).all()),
    }


def build_case(scenario: str, index: int, rng: random.Random) -> dict[str, Any]:
    case_id = f"{OPERATOR}_{scenario}_{index:04d}"
    length = SIGNAL_LENGTHS[index % len(SIGNAL_LENGTHS)]

    raw = generate_signal(scenario, length, rng)
    if scenario == "image_2d":
        img, meta = raw
        return {
            "version": 1,
            "case_id": case_id,
            "task": "fft_1d",
            "operator": OPERATOR,
            "scenario": scenario,
            "inputs": {
                "input_type": "image",
                "image": "input_image.png",
            },
            "expected": {
                "is_finite": meta["is_finite"],
                "image_shape": meta["image_shape"],
            },
            "meta": meta,
            "metrics": [
                "IsFinite",
                "OutputShapeCorrect",
                "RuntimeMs",
                "MemoryAllocation",
            ],
            "image": img,
        }

    signal, meta = raw
    expected = compute_expected_fft(signal)

    # Merge meta into expected carefully: computed FFT metrics take precedence
    # over generator metadata (e.g. dominant_index must come from np.argmax)
    for key, value in meta.items():
        if key not in expected:
            expected[key] = value

    return {
        "version": 1,
        "case_id": case_id,
        "task": "fft_1d",
        "operator": OPERATOR,
        "scenario": scenario,
        "inputs": {
            "input_type": "signal",
            "signal": signal.tolist(),
        },
        "expected": expected,
        "meta": meta,
        "metrics": [
            "DominantIndexError",
            "DcMagnitudeError",
            "MaxMagnitudeError",
            "ReconstructionRmse",
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
    (output_root / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return cases


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate FFT1D golden cases.")
    parser.add_argument("--output", type=Path, default=Path("quality/synthetic/cases/fft"))
    parser.add_argument("--count", type=int, default=120, help="Total cases; rounded down by scenario.")
    parser.add_argument("--seed", type=int, default=4205)
    args = parser.parse_args()

    cases = generate(args.output, args.count, args.seed)
    print(f"Generated {len(cases)} FFT1D cases under {args.output}")


if __name__ == "__main__":
    main()
