#!/usr/bin/env python3
"""Generate synthetic golden cases for the InverseFFT1D operator.

Each case writes one folder:
    <output>/InverseFFT1D/<case_id>/input.json
    <output>/InverseFFT1D/<case_id>/expected.json
    <output>/InverseFFT1D/<case_id>/input_image.png   (for image round-trip cases)
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

OPERATOR = "InverseFFT1D"

SCENARIOS = (
    "zero_spectrum",
    "dc_only",
    "single_frequency",
    "multi_frequency",
    "impulse_signal",
    "random_real_signal",
    "complex_spectrum",
    "output_size_truncate",
    "image_round_trip",
)

SIGNAL_LENGTHS = (64, 128, 256, 512)


def _rng_array(rng: random.Random, length: int, scale: float = 1.0) -> np.ndarray:
    return np.array([rng.gauss(0.0, scale) for _ in range(length)], dtype=np.float64)


def _signal_for_scenario(scenario: str, length: int, rng: random.Random) -> tuple[np.ndarray, dict[str, Any]]:
    t = np.arange(length)
    signal = np.zeros(length, dtype=np.float64)
    meta: dict[str, Any] = {}

    if scenario == "zero_spectrum":
        meta["source"] = "all spectrum bins are zero"

    elif scenario == "dc_only":
        value = rng.choice([-3.0, -1.0, 1.0, 5.0])
        signal[:] = value
        meta["constant_value"] = value

    elif scenario == "single_frequency":
        freq = rng.randint(1, length // 4)
        amplitude = rng.uniform(0.5, 6.0)
        phase = rng.uniform(0.0, 2.0 * math.pi)
        signal = amplitude * np.sin(2.0 * math.pi * freq * t / length + phase)
        meta.update({"frequency": freq, "amplitude": amplitude, "phase": phase})

    elif scenario == "multi_frequency":
        freqs = rng.sample(range(1, length // 3), k=rng.randint(2, 4))
        for freq in freqs:
            amplitude = rng.uniform(0.5, 4.0)
            phase = rng.uniform(0.0, 2.0 * math.pi)
            signal += amplitude * np.sin(2.0 * math.pi * freq * t / length + phase)
        meta["frequencies"] = sorted(freqs)

    elif scenario == "impulse_signal":
        position = rng.randint(0, length - 1)
        amplitude = rng.choice([-2.0, 1.0, 3.0])
        signal[position] = amplitude
        meta.update({"impulse_position": position, "amplitude": amplitude})

    elif scenario == "random_real_signal":
        signal = _rng_array(rng, length, scale=1.5)
        meta["source"] = "random real-valued signal"

    elif scenario == "output_size_truncate":
        signal = (
            2.0 * np.sin(2.0 * math.pi * 3 * t / length)
            + 0.75 * np.sin(2.0 * math.pi * 11 * t / length)
            + 0.1 * _rng_array(rng, length)
        )
        meta["output_size"] = length // 2

    else:
        raise ValueError(f"Unsupported real-signal scenario: {scenario}")

    return signal, meta


def _complex_spectrum(length: int, rng: random.Random) -> tuple[np.ndarray, dict[str, Any]]:
    real = _rng_array(rng, length, scale=2.0)
    imaginary = _rng_array(rng, length, scale=1.25)
    spectrum = real + 1j * imaginary
    return spectrum, {"source": "non-conjugate complex spectrum"}


def _image_case(index: int, rng: random.Random) -> tuple[np.ndarray, dict[str, Any]]:
    size = 64 + (index % 3) * 16
    image = np.full((size, size), 96, dtype=np.uint8)
    stripe_freq = rng.randint(2, 7)
    for y in range(size):
        image[y, :] = np.clip(96 + int(90 * math.sin(2.0 * math.pi * stripe_freq * y / size)), 0, 255)
    cv2.circle(image, (size // 2, size // 2), size // 5, 210, thickness=-1)
    cv2.rectangle(image, (size // 8, size // 8), (size // 8 + 10, size // 8 + 18), 30, thickness=-1)
    return image, {"image_shape": [size, size], "stripe_frequency": stripe_freq}


def _round_list(values: np.ndarray, digits: int = 10) -> list[float]:
    return [round(float(value), digits) for value in values]


def _expected_from_time_domain(reconstructed: np.ndarray) -> dict[str, Any]:
    real = reconstructed.real.astype(np.float64)
    imaginary = reconstructed.imag.astype(np.float64)
    energy = float(np.sum(real * real + imaginary * imaginary))
    return {
        "signal_length": int(real.size),
        "real": _round_list(real),
        "imaginary": _round_list(imaginary),
        "max_imaginary_abs": round(float(np.max(np.abs(imaginary))) if imaginary.size else 0.0, 10),
        "energy": round(energy, 8),
        "is_finite": bool(np.isfinite(real).all() and np.isfinite(imaginary).all()),
    }


def build_case(scenario: str, index: int, rng: random.Random) -> dict[str, Any]:
    case_id = f"{OPERATOR}_{scenario}_{index:04d}"
    length = SIGNAL_LENGTHS[index % len(SIGNAL_LENGTHS)]

    if scenario == "image_round_trip":
        image, meta = _image_case(index, rng)
        return {
            "version": 1,
            "case_id": case_id,
            "task": "inverse_fft_1d",
            "operator": OPERATOR,
            "scenario": scenario,
            "inputs": {
                "input_type": "image_source",
                "image": "input_image.png",
            },
            "expected": {
                "image_shape": meta["image_shape"],
                "image_rmse": 0.0,
                "is_finite": True,
            },
            "meta": meta,
            "metrics": [
                "ImageRmse",
                "IsFinite",
                "OutputShapeCorrect",
                "RuntimeMs",
                "MemoryAllocation",
            ],
            "image": image,
        }

    if scenario == "complex_spectrum":
        spectrum, meta = _complex_spectrum(length, rng)
        output_size = None
    else:
        signal, meta = _signal_for_scenario(scenario, length, rng)
        spectrum = np.fft.fft(signal)
        output_size = meta.get("output_size")

    effective_spectrum = spectrum[:output_size] if output_size else spectrum
    expected = _expected_from_time_domain(np.fft.ifft(effective_spectrum))

    inputs: dict[str, Any] = {
        "input_type": "complex_array",
        "spectrum": {
            "real": _round_list(spectrum.real),
            "imaginary": _round_list(spectrum.imag),
        },
    }
    if output_size:
        inputs["output_size"] = int(output_size)

    return {
        "version": 1,
        "case_id": case_id,
        "task": "inverse_fft_1d",
        "operator": OPERATOR,
        "scenario": scenario,
        "inputs": inputs,
        "expected": expected,
        "meta": meta,
        "metrics": [
            "SignalLengthCorrect",
            "MaxRealError",
            "RmseReal",
            "MaxImaginaryError",
            "ImaginaryMaxAbs",
            "EnergyError",
            "IsFinite",
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
    (output_root / "manifest_inverse_fft.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return cases


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate InverseFFT1D golden cases.")
    parser.add_argument("--output", type=Path, default=Path("quality/synthetic/cases/fft"))
    parser.add_argument("--count", type=int, default=120, help="Total cases; rounded down by scenario.")
    parser.add_argument("--seed", type=int, default=4307)
    args = parser.parse_args()

    cases = generate(args.output, args.count, args.seed)
    print(f"Generated {len(cases)} InverseFFT1D cases under {args.output}")


if __name__ == "__main__":
    main()
