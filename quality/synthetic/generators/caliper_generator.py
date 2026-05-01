#!/usr/bin/env python3
"""Generate synthetic golden cases for CaliperTool.

Each case writes:
    <output>/CaliperTool/<case_id>/input.json
    <output>/CaliperTool/<case_id>/expected.json
    <output>/CaliperTool/<case_id>/image.png

The synthetic scenes are intentionally simple: filled bright or dark bars are
drawn across the operator scan line, so the expected edge-pair width is known
from geometry. Stress cases vary contrast, blur, noise, ROI placement,
polarity, PairDirection, ExpectedCount, and sub-pixel mode.
"""

from __future__ import annotations

import argparse
import json
import math
import random
import shutil
from pathlib import Path
from typing import Any

import cv2
import numpy as np

OPERATOR = "CaliperTool"
IMAGE_W, IMAGE_H = 512, 384
DEFAULT_CENTER = (256.0, 192.0)
SUPERSAMPLE = 8

SCENARIOS = (
    "horizontal_bright_bar",
    "vertical_bright_bar",
    "custom_angle_bar",
    "dark_bar_polarity",
    "low_contrast",
    "blurred_edge",
    "light_noise",
    "strong_noise",
    "multi_edge_roi",
    "close_edges",
    "roi_boundary",
    "wrong_polarity",
    "expected_count_failure",
)

NON_SUBPIXEL_THRESHOLDS = (6.0, 18.0, 28.0)
SUBPIXEL_THRESHOLDS = (4.0, 6.0, 8.0)
CUSTOM_ANGLES = (0.0, 5.0, 15.0, 30.0, 45.0)
WIDTHS = (24.0, 32.0, 40.0, 56.0, 72.0)


def rect_around(
    center: tuple[float, float],
    width: int,
    height: int,
    image_w: int = IMAGE_W,
    image_h: int = IMAGE_H,
) -> dict[str, int]:
    x = int(round(center[0] - width / 2))
    y = int(round(center[1] - height / 2))
    x = max(0, min(image_w - 1, x))
    y = max(0, min(image_h - 1, y))
    width = max(1, min(width, image_w - x))
    height = max(1, min(height, image_h - y))
    return {"X": x, "Y": y, "Width": width, "Height": height}


def downsample(hi_res: np.ndarray) -> np.ndarray:
    return cv2.resize(hi_res, (IMAGE_W, IMAGE_H), interpolation=cv2.INTER_AREA)


def draw_oriented_bar(
    image: np.ndarray,
    center: tuple[float, float],
    width_along_scan: float,
    length_across_scan: float,
    angle_deg: float,
    value: int,
) -> None:
    """Draw a bar whose measured width lies along the scan direction."""
    scale = SUPERSAMPLE
    cx, cy = center[0] * scale, center[1] * scale
    half_w = width_along_scan * scale / 2.0
    half_l = length_across_scan * scale / 2.0
    rad = math.radians(angle_deg)
    dx, dy = math.cos(rad), math.sin(rad)
    nx, ny = -dy, dx

    points = np.array(
        [
            [cx - dx * half_w - nx * half_l, cy - dy * half_w - ny * half_l],
            [cx + dx * half_w - nx * half_l, cy + dy * half_w - ny * half_l],
            [cx + dx * half_w + nx * half_l, cy + dy * half_w + ny * half_l],
            [cx - dx * half_w + nx * half_l, cy - dy * half_w + ny * half_l],
        ],
        dtype=np.float32,
    )
    cv2.fillConvexPoly(image, np.round(points).astype(np.int32), int(value), lineType=cv2.LINE_AA)


def draw_single_step(
    image: np.ndarray,
    center: tuple[float, float],
    angle_deg: float,
    value: int,
) -> None:
    """Draw one half-plane transition through center to create a single edge."""
    scale = SUPERSAMPLE
    cx, cy = center[0] * scale, center[1] * scale
    rad = math.radians(angle_deg)
    dx, dy = math.cos(rad), math.sin(rad)
    nx, ny = -dy, dx
    half_l = max(IMAGE_W, IMAGE_H) * scale
    half_w = max(IMAGE_W, IMAGE_H) * scale
    points = np.array(
        [
            [cx, cy],
            [cx + dx * half_w, cy + dy * half_w],
            [cx + dx * half_w + nx * half_l, cy + dy * half_w + ny * half_l],
            [cx + nx * half_l, cy + ny * half_l],
        ],
        dtype=np.float32,
    )
    cv2.fillConvexPoly(image, np.round(points).astype(np.int32), int(value), lineType=cv2.LINE_AA)


def add_noise(image: np.ndarray, sigma: float, rng: random.Random) -> np.ndarray:
    np_rng = np.random.default_rng(rng.randint(0, 2**31 - 1))
    noise = np_rng.normal(0.0, sigma, size=image.shape)
    return np.clip(image.astype(np.float32) + noise, 0, 255).astype(np.uint8)


def make_base(bg: int) -> np.ndarray:
    return np.full((IMAGE_H * SUPERSAMPLE, IMAGE_W * SUPERSAMPLE), int(bg), dtype=np.uint8)


def base_params(
    direction: str,
    angle: float,
    pair_direction: str,
    index: int,
    expected_count: int = 1,
    polarity: str = "Both",
    edge_threshold: float | None = None,
) -> dict[str, Any]:
    subpixel = index % 2 == 1
    threshold_cycle = SUBPIXEL_THRESHOLDS if subpixel else NON_SUBPIXEL_THRESHOLDS
    return {
        "Direction": direction,
        "Angle": round(angle, 3),
        "Polarity": polarity,
        "EdgeThreshold": edge_threshold if edge_threshold is not None else threshold_cycle[index % len(threshold_cycle)],
        "ExpectedCount": expected_count,
        "MeasureMode": "edge_pairs",
        "PairDirection": pair_direction,
        "SubpixelAccuracy": subpixel,
        "SubPixelMode": "zernike" if subpixel and index % 4 == 3 else "gradient_centroid",
    }


def tolerance_for(scenario: str, subpixel: bool) -> float:
    if scenario in {"horizontal_bright_bar", "vertical_bright_bar", "dark_bar_polarity"}:
        return 0.35 if subpixel else 0.75
    if scenario == "custom_angle_bar":
        return 1.0
    if scenario == "light_noise":
        return 1.25
    if scenario == "strong_noise":
        return 2.5
    if scenario in {"blurred_edge", "low_contrast", "roi_boundary", "close_edges"}:
        return 1.5
    if scenario == "multi_edge_roi":
        return 2.0
    return 1.0


def success_expected(
    width: float,
    pair_count: int,
    distances: list[float],
    scenario: str,
    params: dict[str, Any],
) -> dict[str, Any]:
    tolerance = tolerance_for(scenario, bool(params["SubpixelAccuracy"]))
    return {
        "is_success": True,
        "width": round(width, 4),
        "pair_count": pair_count,
        "pair_distances": [round(value, 4) for value in distances],
        "width_tolerance_px": tolerance,
        "pair_distance_tolerance_px": tolerance,
        "max_stddev_px": 20.0 if pair_count > 1 else 0.05,
    }


def failure_expected(reason: str = "[NoFeature]") -> dict[str, Any]:
    return {
        "is_success": False,
        "expected_error_contains": reason,
    }


def build_case(scenario: str, index: int, rng: random.Random) -> tuple[dict[str, Any], np.ndarray]:
    case_id = f"{OPERATOR}_{scenario}_{index:04d}"
    center = DEFAULT_CENTER
    width = WIDTHS[index % len(WIDTHS)]
    length = 240.0
    bg, fg = 32, 224
    direction = "Horizontal"
    angle = 0.0
    pair_direction = "positive_to_negative"
    roi = rect_around(center, 260, 170)
    expected_distances = [width]
    expected_pair_count = 1
    image: np.ndarray
    meta: dict[str, Any] = {"geometric_width_px": width}

    if scenario == "vertical_bright_bar":
        direction = "Vertical"
        angle = 90.0
        roi = rect_around(center, 180, 260)
    elif scenario == "custom_angle_bar":
        direction = "Custom"
        angle = CUSTOM_ANGLES[index % len(CUSTOM_ANGLES)]
        roi = rect_around(center, 260, 210)
    elif scenario == "dark_bar_polarity":
        bg, fg = 224, 32
        pair_direction = "negative_to_positive"
    elif scenario == "low_contrast":
        bg, fg = 96, 150
        width = 36.0 + (index % 4) * 8.0
        expected_distances = [width]
    elif scenario == "blurred_edge":
        width = 40.0 + (index % 3) * 8.0
        expected_distances = [width]
    elif scenario == "light_noise":
        width = 32.0 + (index % 5) * 6.0
        expected_distances = [width]
    elif scenario == "strong_noise":
        width = 44.0 + (index % 4) * 6.0
        expected_distances = [width]
    elif scenario == "close_edges":
        width = 8.0 + (index % 4) * 2.0
        expected_distances = [width]
    elif scenario == "roi_boundary":
        center = (190.0 + (index % 3) * 6.0, DEFAULT_CENTER[1])
        width = 36.0 + (index % 4) * 6.0
        expected_distances = [width]
        # Keep the left edge close to the ROI start without placing it exactly
        # on the endpoint, where derivative peak detection has no left sample.
        left_edge = center[0] - width / 2.0
        roi = {"X": int(round(left_edge - 5)), "Y": 116, "Width": 150, "Height": 152}
    elif scenario == "multi_edge_roi":
        width_a = 26.0 + (index % 3) * 4.0
        width_b = 36.0 + (index % 4) * 3.0
        expected_distances = [width_a, width_b]
        width = sum(expected_distances) / len(expected_distances)
        expected_pair_count = 2
        roi = rect_around(center, 340, 170)
    elif scenario == "wrong_polarity":
        # Single-polarity filtering leaves only one side of a stripe, so no
        # complete edge pair should be reported.
        bg, fg = (32, 224) if index % 2 == 0 else (224, 32)
        pair_direction = "positive_to_negative" if fg > bg else "negative_to_positive"
    elif scenario == "expected_count_failure":
        if index % 2 == 0:
            expected_distances = []
            width = 0.0
        else:
            expected_distances = [28.0, 34.0]
            width = sum(expected_distances) / len(expected_distances)
        expected_pair_count = 0

    params = base_params(direction, angle, pair_direction, index, expected_count=expected_pair_count)

    if scenario == "low_contrast":
        params["EdgeThreshold"] = 4.0 if params["SubpixelAccuracy"] else 4.0 + (index % 3) * 2.0
    elif scenario == "blurred_edge":
        params["EdgeThreshold"] = 6.0 if params["SubpixelAccuracy"] else 12.0
    elif scenario == "strong_noise":
        params["EdgeThreshold"] = 8.0 if params["SubpixelAccuracy"] else 18.0
    elif scenario == "multi_edge_roi":
        params["ExpectedCount"] = 2
        params["EdgeThreshold"] = 6.0 if params["SubpixelAccuracy"] else 18.0
    elif scenario == "wrong_polarity":
        params["Polarity"] = "DarkToLight" if index % 2 == 0 else "LightToDark"
    elif scenario == "expected_count_failure":
        params["ExpectedCount"] = 1 if index % 2 == 0 else 5

    hi_res = make_base(bg)
    draw_angle = angle
    if direction == "Vertical":
        draw_angle = 90.0

    if scenario == "multi_edge_roi":
        offsets = (-54.0, 58.0)
        for stripe_width, offset in zip(expected_distances, offsets):
            draw_oriented_bar(
                hi_res,
                (center[0] + offset, center[1]),
                stripe_width,
                length,
                0.0,
                fg,
            )
    elif scenario == "expected_count_failure" and index % 2 == 0:
        draw_single_step(hi_res, center, 0.0, fg)
    elif scenario == "expected_count_failure":
        for stripe_width, offset in zip(expected_distances, (-44.0, 48.0)):
            draw_oriented_bar(hi_res, (center[0] + offset, center[1]), stripe_width, length, 0.0, fg)
    else:
        draw_oriented_bar(hi_res, center, expected_distances[0], length, draw_angle, fg)

    image = downsample(hi_res)

    if scenario == "blurred_edge":
        sigma = 1.0 + (index % 4) * 0.35
        image = cv2.GaussianBlur(image, (0, 0), sigmaX=sigma)
        meta["blur_sigma"] = round(sigma, 3)
    elif scenario == "light_noise":
        image = add_noise(image, 4.0, rng)
        meta["noise_sigma"] = 4.0
    elif scenario == "strong_noise":
        image = add_noise(image, 12.0, rng)
        meta["noise_sigma"] = 12.0

    if scenario == "wrong_polarity":
        expected = failure_expected()
    elif scenario == "expected_count_failure":
        expected = failure_expected()
    else:
        expected = success_expected(width, expected_pair_count, expected_distances, scenario, params)

    if scenario == "expected_count_failure":
        meta["available_pair_count"] = 0 if index % 2 == 0 else 2
        meta["required_expected_count"] = params["ExpectedCount"]
    elif scenario == "wrong_polarity":
        meta["polarity_filter"] = params["Polarity"]

    case = {
        "version": 1,
        "case_id": case_id,
        "task": "caliper_tool",
        "operator": OPERATOR,
        "scenario": scenario,
        "params": params,
        "inputs": {
            "image": "image.png",
            "search_region": roi,
        },
        "expected": expected,
        "meta": meta,
        "metrics": [
            "WidthErrorPx",
            "EdgePositionErrorPx",
            "PairCountAccuracy",
            "ExpectedCountFailureCorrectness",
            "UncertaintyPxCalibration",
            "RuntimeMs",
            "MemoryAllocation",
        ],
    }

    return case, image


def write_case(output_root: Path, case: dict[str, Any], image: np.ndarray) -> None:
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
            "params",
            "inputs",
            "meta",
            "metrics",
        )
    }
    expected_payload = {
        "case_id": case["case_id"],
        "task": case["task"],
        "operator": case["operator"],
        "expected": case["expected"],
    }

    (case_dir / "input.json").write_text(json.dumps(input_payload, indent=2), encoding="utf-8")
    (case_dir / "expected.json").write_text(json.dumps(expected_payload, indent=2), encoding="utf-8")
    cv2.imwrite(str(case_dir / "image.png"), image)


def generate(output_root: Path, count: int, seed: int) -> list[dict[str, Any]]:
    rng = random.Random(seed)
    cases: list[dict[str, Any]] = []
    operator_dir = output_root / OPERATOR
    if operator_dir.exists():
        shutil.rmtree(operator_dir)

    per_scenario = max(1, count // len(SCENARIOS))
    for scenario in SCENARIOS:
        for index in range(per_scenario):
            case, image = build_case(scenario, index, rng)
            write_case(output_root, case, image)
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
                "expected_success": case["expected"]["is_success"],
                "expected_width": case["expected"].get("width"),
                "expected_pair_count": case["params"].get("ExpectedCount"),
            }
            for case in cases
        ],
    }
    output_root.mkdir(parents=True, exist_ok=True)
    (output_root / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return cases


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate CaliperTool golden cases.")
    parser.add_argument("--output", type=Path, default=Path("quality/synthetic/cases/caliper_tool"))
    parser.add_argument("--count", type=int, default=117, help="Total cases; rounded down by scenario.")
    parser.add_argument("--seed", type=int, default=4401)
    args = parser.parse_args()

    cases = generate(args.output, args.count, args.seed)
    print(f"Generated {len(cases)} CaliperTool cases under {args.output}")


if __name__ == "__main__":
    main()
