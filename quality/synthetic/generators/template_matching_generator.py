#!/usr/bin/env python3
"""Generate synthetic golden cases for TemplateMatching.

Each case writes:
    <output>/TemplateMatching/<case_id>/input.json
    <output>/TemplateMatching/<case_id>/expected.json
    <output>/TemplateMatching/<case_id>/scene.png
    <output>/TemplateMatching/<case_id>/template.png
    <output>/TemplateMatching/<case_id>/mask.png  (optional)

The cases target the operator card contract: fixed-scale template matching,
Gray/Edge/Gradient domains, ROI and Mask constraints, multi-match NMS, and the
Score / NormalizedScore / RawResponse semantics for all supported methods.
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

OPERATOR = "TemplateMatching"
SCENE_W, SCENE_H = 256, 224
TEMPLATE_SIZE = 40

SCENARIOS = (
    "translation_gray",
    "method_contract",
    "sqdiff_contract",
    "roi_constraint",
    "mask_constraint",
    "roi_mask_constraint",
    "multi_match_nms",
    "edge_domain",
    "gradient_domain",
    "illumination_shift",
    "repeated_texture",
    "low_texture",
    "fixed_scale_boundary",
)

METHODS = ("CCoeffNormed", "SqDiff", "SqDiffNormed", "CCorr", "CCorrNormed", "CCoeff")
DOMAINS = ("Gray", "Edge", "Gradient")


def create_pattern_template(size: int = TEMPLATE_SIZE, variant: int = 0) -> np.ndarray:
    image = np.full((size, size), 28, dtype=np.uint8)
    cv2.rectangle(image, (5, 5), (size - 6, size - 6), 225, -1)
    cv2.line(image, (5, size // 2), (size - 6, size // 2), 34, 2)
    cv2.circle(image, (size // 2 - 5, size // 2 - 8), 5, 42, -1)
    cv2.rectangle(image, (size // 2 + 4, size // 2 + 2), (size - 10, size - 9), 95, -1)
    if variant % 3 == 1:
        cv2.line(image, (9, 9), (size - 9, size - 9), 70, 2)
    elif variant % 3 == 2:
        cv2.circle(image, (size - 13, 13), 4, 15, -1)
    return image


def create_broad_template(size: int = 56) -> np.ndarray:
    image = np.full((size, size), 42, dtype=np.uint8)
    cv2.rectangle(image, (5, 5), (size - 6, size - 6), 220, -1)
    cv2.rectangle(image, (16, 14), (28, 26), 70, -1)
    cv2.line(image, (8, size - 14), (size - 9, size - 20), 110, 2)
    return image


def copy_template(scene: np.ndarray, template: np.ndarray, x: int, y: int) -> None:
    h, w = template.shape[:2]
    scene[y : y + h, x : x + w] = template


def shifted_template(template: np.ndarray, alpha: float, beta: float) -> np.ndarray:
    return np.clip(template.astype(np.float32) * alpha + beta, 0, 255).astype(np.uint8)


def rotated_template(template: np.ndarray, angle: float) -> np.ndarray:
    h, w = template.shape[:2]
    matrix = cv2.getRotationMatrix2D((w / 2.0, h / 2.0), angle, 1.0)
    return cv2.warpAffine(template, matrix, (w, h), flags=cv2.INTER_LINEAR, borderValue=28)


def scaled_template(template: np.ndarray, scale: float) -> np.ndarray:
    h, w = template.shape[:2]
    scaled = cv2.resize(template, None, fx=scale, fy=scale, interpolation=cv2.INTER_LINEAR)
    canvas = np.full_like(template, 28)
    sh, sw = scaled.shape[:2]
    if sh <= h and sw <= w:
        x = (w - sw) // 2
        y = (h - sh) // 2
        canvas[y : y + sh, x : x + sw] = scaled
    else:
        x = (sw - w) // 2
        y = (sh - h) // 2
        canvas[:, :] = scaled[y : y + h, x : x + w]
    return canvas


def make_mask(rects: list[tuple[int, int, int, int]]) -> np.ndarray:
    mask = np.zeros((SCENE_H, SCENE_W), dtype=np.uint8)
    for x, y, w, h in rects:
        cv2.rectangle(mask, (x, y), (x + w - 1, y + h - 1), 255, -1)
    return mask


def center_position(top_left: tuple[int, int], template: np.ndarray) -> dict[str, float]:
    return {
        "x": top_left[0] + template.shape[1] / 2.0,
        "y": top_left[1] + template.shape[0] / 2.0,
    }


def method_threshold(method: str, expected_match: bool = True) -> float:
    if not expected_match:
        return 0.93
    if method in {"SqDiff", "SqDiffNormed"}:
        return 0.96
    if method in {"CCoeffNormed", "CCorrNormed"}:
        return 0.78
    return 0.0


def default_params(
    method: str = "CCoeffNormed",
    domain: str = "Gray",
    threshold: float | None = None,
    max_matches: int = 1,
) -> dict[str, Any]:
    return {
        "Method": method,
        "Domain": domain,
        "Threshold": method_threshold(method) if threshold is None else threshold,
        "MaxMatches": max_matches,
        "UseRoi": False,
        "RoiX": 0,
        "RoiY": 0,
        "RoiWidth": 0,
        "RoiHeight": 0,
        "OriginMode": "Center",
        "OriginX": 0.0,
        "OriginY": 0.0,
    }


def expected_match(
    positions: list[dict[str, float]],
    match_count: int,
    method: str,
    domain: str,
    tolerance: float = 1.0,
    allowed_positions: list[dict[str, float]] | None = None,
    min_score: float = 0.75,
    require_distinct: bool = False,
) -> dict[str, Any]:
    return {
        "is_match": True,
        "positions": positions,
        "allowed_positions": allowed_positions or positions,
        "match_count": match_count,
        "position_tolerance_px": tolerance,
        "min_normalized_score": min_score,
        "method": method if domain == "Gray" else f"{method}:{domain}",
        "score_contract": method,
        "require_distinct": require_distinct,
    }


def expected_no_match(reason: str, method: str, domain: str) -> dict[str, Any]:
    return {
        "is_match": False,
        "match_count": 0,
        "expected_failure_contains": reason,
        "method": method if domain == "Gray" else f"{method}:{domain}",
    }


def build_case(scenario: str, index: int, rng: random.Random) -> tuple[dict[str, Any], np.ndarray, np.ndarray, np.ndarray | None]:
    case_id = f"{OPERATOR}_{scenario}_{index:04d}"
    scene = np.full((SCENE_H, SCENE_W), 18, dtype=np.uint8)
    template = create_pattern_template(variant=index)
    mask: np.ndarray | None = None
    method = "CCoeffNormed"
    domain = "Gray"
    params = default_params()
    expected: dict[str, Any]
    meta: dict[str, Any] = {}

    if scenario == "translation_gray":
        top_left = (30 + (index % 3) * 42, 34 + (index // 3) * 36)
        method = "CCoeffNormed"
        params = default_params(method=method, threshold=0.80)
        copy_template(scene, template, *top_left)
        expected = expected_match([center_position(top_left, template)], 1, method, domain, min_score=0.95)

    elif scenario == "method_contract":
        method = METHODS[index % len(METHODS)]
        top_left = (44 + (index % 3) * 38, 50 + (index // 3) * 34)
        params = default_params(method=method, threshold=method_threshold(method))
        copy_template(scene, template, *top_left)
        expected = expected_match([center_position(top_left, template)], 1, method, domain, min_score=0.90)

    elif scenario == "sqdiff_contract":
        method = "SqDiff" if index % 2 == 0 else "SqDiffNormed"
        top_left = (58 + (index % 3) * 30, 42 + (index // 3) * 34)
        params = default_params(method=method, threshold=0.96)
        copy_template(scene, template, *top_left)
        expected = expected_match([center_position(top_left, template)], 1, method, domain, min_score=0.96)

    elif scenario == "roi_constraint":
        target = (132, 116)
        decoy = (24, 26)
        copy_template(scene, template, *decoy)
        copy_template(scene, template, *target)
        params = default_params(threshold=0.80)
        params.update({"UseRoi": True, "RoiX": 110, "RoiY": 96, "RoiWidth": 90, "RoiHeight": 84})
        expected = expected_match([center_position(target, template)], 1, method, domain, min_score=0.95)
        meta["decoy_position"] = center_position(decoy, template)

    elif scenario == "mask_constraint":
        target = (142, 130)
        decoy = (28, 34)
        copy_template(scene, template, *decoy)
        copy_template(scene, template, *target)
        mask = make_mask([(target[0], target[1], template.shape[1], template.shape[0])])
        params = default_params(threshold=0.80)
        expected = expected_match([center_position(target, template)], 1, method, domain, min_score=0.95)
        meta["decoy_position"] = center_position(decoy, template)

    elif scenario == "roi_mask_constraint":
        target = (112, 92)
        decoy = (180, 34)
        copy_template(scene, shifted_template(template, 1.05, 8), *target)
        copy_template(scene, template, *decoy)
        params = default_params(domain="Gradient", threshold=0.42)
        params.update({"UseRoi": True, "RoiX": 84, "RoiY": 72, "RoiWidth": 110, "RoiHeight": 100})
        domain = "Gradient"
        mask = make_mask([(target[0] - 4, target[1] - 4, template.shape[1] + 8, template.shape[0] + 8)])
        expected = expected_match([center_position(target, template)], 1, method, domain, tolerance=1.5, min_score=0.45)
        meta["decoy_position"] = center_position(decoy, template)

    elif scenario == "multi_match_nms":
        positions = [(20, 28), (158, 136)]
        for top_left in positions:
            copy_template(scene, template, *top_left)
        params = default_params(threshold=0.74, max_matches=2)
        expected = expected_match(
            [center_position(p, template) for p in positions],
            2,
            method,
            domain,
            tolerance=1.0,
            min_score=0.90,
            require_distinct=True,
        )

    elif scenario == "edge_domain":
        domain = "Edge"
        top_left = (70 + (index % 3) * 20, 64 + (index // 3) * 22)
        params = default_params(domain=domain, threshold=0.55)
        shifted = shifted_template(template, 0.55, 110)
        copy_template(scene, shifted, *top_left)
        expected = expected_match([center_position(top_left, template)], 1, method, domain, tolerance=2.0, min_score=0.55)

    elif scenario == "gradient_domain":
        domain = "Gradient"
        top_left = (68 + (index % 3) * 22, 58 + (index // 3) * 24)
        params = default_params(domain=domain, threshold=0.42)
        shifted = shifted_template(template, 1.18, 24)
        copy_template(scene, shifted, *top_left)
        expected = expected_match([center_position(top_left, template)], 1, method, domain, tolerance=1.5, min_score=0.45)

    elif scenario == "illumination_shift":
        top_left = (54 + (index % 3) * 34, 70 + (index // 3) * 24)
        alpha = 0.65 + (index % 3) * 0.18
        beta = 45 + (index // 3) * 10
        params = default_params(method="CCoeffNormed", threshold=0.72)
        copy_template(scene, shifted_template(template, alpha, beta), *top_left)
        expected = expected_match([center_position(top_left, template)], 1, method, domain, tolerance=1.5, min_score=0.80)
        meta.update({"alpha": round(alpha, 3), "beta": beta})

    elif scenario == "repeated_texture":
        positions = [(18, 24), (86, 24), (154, 24), (52, 116), (134, 122)]
        for top_left in positions:
            copy_template(scene, template, *top_left)
        params = default_params(threshold=0.72, max_matches=3)
        expected = expected_match(
            [center_position(p, template) for p in positions[:3]],
            3,
            method,
            domain,
            tolerance=1.0,
            allowed_positions=[center_position(p, template) for p in positions],
            min_score=0.90,
            require_distinct=True,
        )
        meta["repeated_target_count"] = len(positions)

    elif scenario == "low_texture":
        template = np.full((TEMPLATE_SIZE, TEMPLATE_SIZE), 128, dtype=np.uint8)
        scene = np.full((SCENE_H, SCENE_W), 128, dtype=np.uint8)
        params = default_params(threshold=0.70)
        expected = expected_no_match("insufficient texture", method, domain)

    elif scenario == "fixed_scale_boundary":
        top_left = (86, 76)
        if index % 2 == 0:
            transformed = scaled_template(template, 1.18)
            meta["boundary_kind"] = "scale_1.18"
        else:
            transformed = rotated_template(template, 12.0)
            meta["boundary_kind"] = "rotation_12deg"
        params = default_params(method="CCoeffNormed", threshold=0.94)
        copy_template(scene, transformed, *top_left)
        expected = expected_no_match("No match above threshold", method, domain)

    else:
        raise ValueError(f"Unknown scenario: {scenario}")

    input_payload = {
        "version": 1,
        "case_id": case_id,
        "task": "template_matching",
        "operator": OPERATOR,
        "scenario": scenario,
        "params": params,
        "inputs": {
            "image": "scene.png",
            "template": "template.png",
        },
        "meta": meta,
        "metrics": [
            "IsMatchCorrect",
            "PositionErrorPx",
            "MatchCountCorrect",
            "ScoreContractCorrect",
            "NormalizedScoreInRange",
            "NmsDistinct",
            "RuntimeMs",
            "MemoryAllocation",
        ],
    }
    if mask is not None:
        input_payload["inputs"]["mask"] = "mask.png"

    case = {
        **input_payload,
        "expected": expected,
    }
    return case, scene, template, mask


def write_case(output_root: Path, case: dict[str, Any], scene: np.ndarray, template: np.ndarray, mask: np.ndarray | None) -> None:
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
    cv2.imwrite(str(case_dir / "scene.png"), scene)
    cv2.imwrite(str(case_dir / "template.png"), template)
    if mask is not None:
        cv2.imwrite(str(case_dir / "mask.png"), mask)


def generate(output_root: Path, count: int, seed: int) -> list[dict[str, Any]]:
    rng = random.Random(seed)
    cases: list[dict[str, Any]] = []
    operator_dir = output_root / OPERATOR
    if operator_dir.exists():
        shutil.rmtree(operator_dir)

    per_scenario = max(1, count // len(SCENARIOS))
    for scenario in SCENARIOS:
        for index in range(per_scenario):
            case, scene, template, mask = build_case(scenario, index, rng)
            write_case(output_root, case, scene, template, mask)
            cases.append(case)

    output_root.mkdir(parents=True, exist_ok=True)
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
                "method": case["params"]["Method"],
                "domain": case["params"]["Domain"],
            }
            for case in cases
        ],
    }
    (output_root / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return cases


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate TemplateMatching golden cases.")
    parser.add_argument("--output", type=Path, default=Path("quality/synthetic/cases/template_matching"))
    parser.add_argument("--count", type=int, default=117, help="Total cases; rounded down by scenario.")
    parser.add_argument("--seed", type=int, default=4501)
    args = parser.parse_args()

    cases = generate(args.output, args.count, args.seed)
    print(f"Generated {len(cases)} TemplateMatching cases under {args.output}")


if __name__ == "__main__":
    main()
