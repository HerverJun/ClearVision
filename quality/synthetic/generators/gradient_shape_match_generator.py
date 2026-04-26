#!/usr/bin/env python3
"""Generate synthetic golden cases for GradientShapeMatch operator.

Each case writes:
    <output>/GradientShapeMatch/<case_id>/input.json
    <output>/GradientShapeMatch/<case_id>/expected.json
    <output>/GradientShapeMatch/<case_id>/template.png
    <output>/GradientShapeMatch/<case_id>/scene.png

The generator produces geometric shapes with strong edges so that gradient-
direction features are abundant. Scenes embed the rotated template at known
locations so that position and angle error can be verified by the runner.
"""

from __future__ import annotations

import argparse
import json
import math
import random
from pathlib import Path
from typing import Iterable

import cv2
import numpy as np

OPERATOR = "GradientShapeMatch"

SCENARIOS = (
    "translation",
    "rotation_small",
    "rotation_large",
    "low_contrast",
    "blurred_edge",
    "partial_occlusion",
    "strong_background",
    "low_feature",
    "roi_search",
)

# Key parameter combos: (AngleRange, AngleStep, MagnitudeThreshold)
PARAM_COMBOS: list[tuple[int, int, int]] = [
    (0, 1, 30),
    (15, 1, 30),
    (30, 1, 30),
    (30, 2, 30),
    (30, 5, 30),
    (60, 1, 30),
    (60, 2, 30),
    (180, 1, 30),
    (180, 5, 30),
    (30, 1, 10),
    (30, 1, 60),
    (30, 1, 100),
]

SCENE_W, SCENE_H = 512, 384
TEMPLATE_SIZE = 160
BG_GRAY = 128


def draw_shape(canvas: np.ndarray, shape: str, color: int, rng: random.Random) -> None:
    """Draw a geometric shape centered on the canvas."""
    h, w = canvas.shape[:2]
    cx, cy = w // 2, h // 2
    scale = min(w, h) // 3

    if shape == "rect":
        half = scale // 2
        cv2.rectangle(canvas, (cx - half, cy - half), (cx + half, cy + half), color, -1)
    elif shape == "circle":
        cv2.circle(canvas, (cx, cy), scale // 2, color, -1)
    elif shape == "triangle":
        pts = np.array([
            [cx, cy - scale // 2],
            [cx - scale // 2, cy + scale // 2],
            [cx + scale // 2, cy + scale // 2],
        ], np.int32)
        cv2.fillPoly(canvas, [pts], color)
    elif shape == "cross":
        t = scale // 4
        cv2.rectangle(canvas, (cx - t, cy - scale // 2), (cx + t, cy + scale // 2), color, -1)
        cv2.rectangle(canvas, (cx - scale // 2, cy - t), (cx + scale // 2, cy + t), color, -1)
    elif shape == "star":
        pts = []
        outer_r = scale // 2
        inner_r = scale // 4
        for i in range(10):
            angle = math.pi / 2 + i * math.pi / 5
            r = outer_r if i % 2 == 0 else inner_r
            pts.append([cx + int(r * math.cos(angle)), cy - int(r * math.sin(angle))])
        cv2.fillPoly(canvas, [np.array(pts, np.int32)], color)
    elif shape == "ring":
        cv2.circle(canvas, (cx, cy), scale // 2, color, scale // 6)
    elif shape == "asym":
        half = scale // 2
        # Asymmetric marker: a block with a small offset tab. This keeps angle
        # well-defined in low-contrast and textured-background stress cases.
        cv2.rectangle(canvas, (cx - half, cy - half), (cx + half // 3, cy + half), color, -1)
        cv2.rectangle(canvas, (cx + half // 3, cy - half), (cx + half, cy - half // 4), color, -1)
        cv2.circle(canvas, (cx + half // 3, cy + half // 2), max(3, scale // 10), color, -1)
    else:
        # default rectangle
        half = scale // 2
        cv2.rectangle(canvas, (cx - half, cy - half), (cx + half, cy + half), color, -1)


def make_template(
    shape: str,
    size: int,
    contrast: str,
    rng: random.Random,
) -> np.ndarray:
    """Create a template image with a centered shape."""
    canvas = np.full((size, size), BG_GRAY, dtype=np.uint8)

    if contrast == "normal":
        fg = 255
    elif contrast == "low":
        fg = 160
    elif contrast == "inverse":
        fg = 0
    else:
        fg = 255

    if shape == "blank":
        # Low-feature: almost no edges
        return canvas

    draw_shape(canvas, shape, int(fg), rng)
    return canvas


def embed_template(
    scene: np.ndarray,
    template: np.ndarray,
    angle: float,
    tx: int,
    ty: int,
) -> tuple[np.ndarray, int, int]:
    """Rotate template and embed it into scene at (tx, ty). Returns (scene, cx, cy)."""
    h, w = template.shape[:2]
    # rotate around center
    # C# GradientShapeMatcher uses standard math CCW rotation matrix.
    # OpenCV getRotationMatrix2D uses the transpose of that matrix (CW in standard coords).
    # We negate angle so that the visual rotation in the image matches C# semantics.
    M = cv2.getRotationMatrix2D((w / 2, h / 2), -angle, 1.0)
    rotated = cv2.warpAffine(template, M, (w, h), borderValue=int(BG_GRAY))

    # compute top-left so that rotated center lands at (tx, ty)
    cx, cy = w // 2, h // 2
    top_left_x = tx - cx
    top_left_y = ty - cy

    # clip
    x1 = max(0, top_left_x)
    y1 = max(0, top_left_y)
    x2 = min(scene.shape[1], top_left_x + w)
    y2 = min(scene.shape[0], top_left_y + h)

    src_x1 = x1 - top_left_x
    src_y1 = y1 - top_left_y
    src_x2 = src_x1 + (x2 - x1)
    src_y2 = src_y1 + (y2 - y1)

    if y2 > y1 and x2 > x1:
        scene[y1:y2, x1:x2] = rotated[src_y1:src_y2, src_x1:src_x2]

    return scene, tx, ty


def add_background_texture(scene: np.ndarray, rng: random.Random) -> np.ndarray:
    """Add strong background texture without destroying edges."""
    np_rng = np.random.default_rng(rng.randint(0, 2**31))
    noise = np_rng.integers(0, 40, size=scene.shape, dtype=np.uint8)
    textured = cv2.add(scene, noise)
    # horizontal stripes
    for y in range(0, scene.shape[0], 8):
        textured[y:y + 2, :] = np.clip(textured[y:y + 2, :].astype(np.int16) + 30, 0, 255).astype(np.uint8)
    return textured


def make_scene(
    template: np.ndarray,
    scenario: str,
    angle: float,
    tx: int,
    ty: int,
    rng: random.Random,
) -> tuple[np.ndarray, dict[str, object]]:
    """Create a scene image and metadata for the given scenario."""
    scene = np.full((SCENE_H, SCENE_W), BG_GRAY, dtype=np.uint8)
    meta: dict[str, object] = {"occlusion": False, "blur_sigma": 0.0}

    if scenario == "roi_search":
        # Place two identical shapes; the intended target is at (tx, ty)
        # Place a decoy at a different location
        decoy_tx = tx + 120 if tx < SCENE_W // 2 else tx - 120
        decoy_ty = ty + 80 if ty < SCENE_H // 2 else ty - 80
        decoy_tx = max(TEMPLATE_SIZE // 2, min(SCENE_W - TEMPLATE_SIZE // 2, decoy_tx))
        decoy_ty = max(TEMPLATE_SIZE // 2, min(SCENE_H - TEMPLATE_SIZE // 2, decoy_ty))
        scene, _, _ = embed_template(scene, template, angle, decoy_tx, decoy_ty)
        scene, cx, cy = embed_template(scene, template, angle, tx, ty)
        meta["decoy_position"] = {"x": decoy_tx, "y": decoy_ty}
    else:
        scene, cx, cy = embed_template(scene, template, angle, tx, ty)

    if scenario == "blurred_edge":
        sigma = rng.uniform(1.0, 2.5)
        scene = cv2.GaussianBlur(scene, (0, 0), sigmaX=sigma)
        meta["blur_sigma"] = round(sigma, 2)

    if scenario == "partial_occlusion":
        occ_w = TEMPLATE_SIZE // 3
        occ_h = TEMPLATE_SIZE // 4
        occ_x = max(0, cx - occ_w // 2 + rng.randint(-10, 10))
        occ_y = max(0, cy - occ_h // 2 + rng.randint(-10, 10))
        cv2.rectangle(scene, (occ_x, occ_y), (occ_x + occ_w, occ_y + occ_h), int(BG_GRAY), -1)
        meta["occlusion"] = True
        meta["occlusion_rect"] = [occ_x, occ_y, occ_w, occ_h]

    if scenario == "strong_background":
        scene = add_background_texture(scene, rng)

    return scene, meta


def choose_params(scenario: str, index: int) -> tuple[int, int, int]:
    """Pick a sensible parameter combo for the scenario."""
    if scenario == "translation":
        return (0, 1, 30)
    if scenario == "rotation_small":
        return PARAM_COMBOS[index % len([c for c in PARAM_COMBOS if c[0] >= 15 and c[0] <= 30])]
    if scenario == "rotation_large":
        return PARAM_COMBOS[index % len([c for c in PARAM_COMBOS if c[0] >= 60])]
    if scenario == "low_contrast":
        # lower threshold helps low contrast
        return PARAM_COMBOS[index % len(PARAM_COMBOS)]
    if scenario == "blurred_edge":
        return PARAM_COMBOS[index % len(PARAM_COMBOS)]
    if scenario == "partial_occlusion":
        return PARAM_COMBOS[index % len(PARAM_COMBOS)]
    if scenario == "strong_background":
        return PARAM_COMBOS[index % len(PARAM_COMBOS)]
    if scenario == "low_feature":
        return (0, 1, 30)
    if scenario == "roi_search":
        return (0, 1, 30)
    return PARAM_COMBOS[index % len(PARAM_COMBOS)]


def choose_angle(scenario: str, index: int, rng: random.Random) -> float:
    """Choose rotation angle in degrees."""
    if scenario == "translation":
        return 0.0
    if scenario == "rotation_small":
        angles = [-30.0, -15.0, -5.0, 5.0, 15.0, 30.0]
        return angles[index % len(angles)]
    if scenario == "rotation_large":
        angles = [-180.0, -90.0, -60.0, 60.0, 90.0, 180.0]
        return angles[index % len(angles)]
    if scenario == "low_feature":
        return 0.0
    if scenario == "roi_search":
        return 0.0
    # others: small random rotation
    return float(rng.randint(-10, 10))


def choose_shape(scenario: str, index: int) -> str:
    shapes = ["rect", "circle", "triangle", "cross", "star", "ring"]
    rotation_shapes = ["triangle"]  # lowest symmetry for reliable angle tests
    if scenario == "low_feature":
        return "blank"
    if scenario == "low_contrast":
        return "asym"
    if scenario in ("rotation_small", "rotation_large"):
        return rotation_shapes[index % len(rotation_shapes)]
    if scenario in ("blurred_edge", "strong_background"):
        return "asym"
    if scenario == "strong_background":
        return "asym"
    return shapes[index % len(shapes)]


def build_case(
    scenario: str,
    index: int,
    rng: random.Random,
) -> tuple[dict[str, object], np.ndarray, np.ndarray] | None:
    case_id = f"{OPERATOR}_{scenario}_{index:04d}"
    shape = choose_shape(scenario, index)
    angle = choose_angle(scenario, index, rng)
    angle_range, angle_step, magnitude_threshold = choose_params(scenario, index)

    # Ensure angle_range covers the target angle
    if abs(angle) > angle_range:
        angle_range = int(math.ceil(abs(angle)))

    contrast = "low" if scenario == "low_contrast" else "normal"
    template = make_template(shape, TEMPLATE_SIZE, contrast, rng)

    # Choose embedding position
    margin = TEMPLATE_SIZE // 2 + 10
    tx = rng.randint(margin, SCENE_W - margin)
    ty = rng.randint(margin, SCENE_H - margin)

    scene, meta = make_scene(template, scenario, angle, tx, ty, rng)

    # Low-feature is expected to fail. Partial occlusion is a capability-boundary
    # case: no-match is acceptable, but a correct match should also pass.
    is_match_expected = scenario != "low_feature"

    # Score threshold expectation: lower for stressed scenes
    score_min = 40.0 if scenario in ("low_contrast", "blurred_edge", "partial_occlusion", "strong_background") else 50.0

    params: dict[str, object] = {
        "MinScore": 60.0,
        "AngleRange": angle_range,
        "AngleStep": angle_step,
        "MagnitudeThreshold": magnitude_threshold,
        "EnableCache": True,
        "UseRoi": scenario == "roi_search",
    }

    if scenario == "roi_search":
        roi_margin = 20
        roi_x = max(0, tx - TEMPLATE_SIZE // 2 - roi_margin)
        roi_y = max(0, ty - TEMPLATE_SIZE // 2 - roi_margin)
        roi_w = min(SCENE_W - roi_x, TEMPLATE_SIZE + roi_margin * 2)
        roi_h = min(SCENE_H - roi_y, TEMPLATE_SIZE + roi_margin * 2)
        params["RoiX"] = roi_x
        params["RoiY"] = roi_y
        params["RoiWidth"] = roi_w
        params["RoiHeight"] = roi_h
        meta["roi"] = {"x": roi_x, "y": roi_y, "width": roi_w, "height": roi_h}

    case = {
        "version": 1,
        "case_id": case_id,
        "task": "gradient_shape_match",
        "operator": OPERATOR,
        "scenario": scenario,
        "params": params,
        "meta": meta,
        "inputs": {
            "template": "template.png",
            "scene": "scene.png",
        },
        "expected": {
            "is_match": is_match_expected,
            "allow_no_match": scenario == "partial_occlusion",
            "angle_optional": scenario == "partial_occlusion",
            "position": {"x": tx, "y": ty},
            "angle": round(angle, 2),
            "score_min": score_min,
        },
        "metrics": [
            "PositionErrorPx",
            "AngleErrorDeg",
            "IsMatchCorrect",
            "ScoreValue",
            "RuntimeMs",
            "MemoryAllocation",
        ],
    }
    if scenario == "low_feature":
        case["expected"]["failure_reason"] = "InvalidTemplate"

    case["meta"]["shape"] = shape
    return case, template, scene


def write_case(output_root: Path, case: dict[str, object], template: np.ndarray, scene: np.ndarray) -> None:
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
            "meta",
            "inputs",
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
    cv2.imwrite(str(case_dir / "template.png"), template)
    cv2.imwrite(str(case_dir / "scene.png"), scene)


def generate(output_root: Path, count: int, seed: int) -> list[dict[str, object]]:
    rng = random.Random(seed)
    cases: list[dict[str, object]] = []

    per_scenario = max(1, count // len(SCENARIOS))

    for scenario in SCENARIOS:
        for i in range(per_scenario):
            result = build_case(scenario, i, rng)
            if result is None:
                continue
            case, template, scene = result
            write_case(output_root, case, template, scene)
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
                "is_match": case["expected"]["is_match"],
                "angle": case["expected"]["angle"],
            }
            for case in cases
        ],
    }
    (output_root / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return cases


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate GradientShapeMatch golden cases.")
    parser.add_argument("--output", type=Path, default=Path("quality/synthetic/cases/gradient_shape_match"))
    parser.add_argument("--count", type=int, default=120, help="Total cases; rounded down by scenario.")
    parser.add_argument("--seed", type=int, default=4204)
    args = parser.parse_args()

    cases = generate(args.output, args.count, args.seed)
    print(f"Generated {len(cases)} GradientShapeMatch cases under {args.output}")


if __name__ == "__main__":
    main()
