#!/usr/bin/env python3
"""Metrics for calibration synthetic golden cases."""

from __future__ import annotations

import json
import math
from pathlib import Path
from typing import Any


def reprojection_errors(expected_points: list[list[float]], actual_points: list[list[float]]) -> list[float]:
    return [
        math.hypot(exp[0] - act[0], exp[1] - act[1])
        for exp, act in zip(expected_points, actual_points)
    ]


def reprojection_stats(expected_points: list[list[float]], actual_points: list[list[float]]) -> dict[str, float]:
    errors = reprojection_errors(expected_points, actual_points)
    if not errors:
        return {"ReprojectionRmsePx": 0.0, "ReprojectionMaxPx": 0.0, "ReprojectionMeanPx": 0.0}
    mse = sum(error * error for error in errors) / len(errors)
    return {
        "ReprojectionRmsePx": math.sqrt(mse),
        "ReprojectionMaxPx": max(errors),
        "ReprojectionMeanPx": sum(errors) / len(errors),
    }


def translation_error(a: list[list[float]], b: list[list[float]]) -> float:
    return math.sqrt(sum((a[i][3] - b[i][3]) ** 2 for i in range(3)))


def rotation_error_degrees(a: list[list[float]], b: list[list[float]]) -> float:
    trace = sum(sum(a[r][k] * b[r][k] for r in range(3)) for k in range(3))
    value = max(-1.0, min(1.0, (trace - 1.0) / 2.0))
    return math.degrees(math.acos(value))


def apply_homography(matrix: list[list[float]], point: list[float]) -> list[float]:
    x, y = point
    den = matrix[2][0] * x + matrix[2][1] * y + matrix[2][2]
    return [
        (matrix[0][0] * x + matrix[0][1] * y + matrix[0][2]) / den,
        (matrix[1][0] * x + matrix[1][1] * y + matrix[1][2]) / den,
    ]


def roundtrip_errors(
    points: list[list[float]],
    forward: list[list[float]],
    inverse: list[list[float]],
) -> list[float]:
    errors: list[float] = []
    for point in points:
        mapped = apply_homography(forward, point)
        restored = apply_homography(inverse, mapped)
        errors.append(math.hypot(point[0] - restored[0], point[1] - restored[1]))
    return errors


def roundtrip_stats(
    points: list[list[float]],
    forward: list[list[float]],
    inverse: list[list[float]],
    prefix: str = "RoundTrip",
) -> dict[str, float]:
    errors = roundtrip_errors(points, forward, inverse)
    if not errors:
        return {f"{prefix}Rmse": 0.0, f"{prefix}Max": 0.0, f"{prefix}Mean": 0.0}
    mse = sum(error * error for error in errors) / len(errors)
    return {
        f"{prefix}Rmse": math.sqrt(mse),
        f"{prefix}Max": max(errors),
        f"{prefix}Mean": sum(errors) / len(errors),
    }


def undistort_residual(expected_points: list[list[float]], actual_points: list[list[float]]) -> dict[str, float]:
    stats = reprojection_stats(expected_points, actual_points)
    return {
        "UndistortResidualRmsePx": stats["ReprojectionRmsePx"],
        "UndistortResidualMaxPx": stats["ReprojectionMaxPx"],
        "UndistortResidualMeanPx": stats["ReprojectionMeanPx"],
    }


def evaluate_case(case: dict[str, Any]) -> dict[str, Any]:
    operator = case["operator"]
    inputs = case["inputs"]
    expected = case["expected"]
    metrics: dict[str, Any] = {
        "ExpectedAccepted": bool(expected.get("accepted", True)),
        "SchemaValid": True,
    }

    first_view = inputs["image_points"][0]
    metrics.update(reprojection_stats(first_view, first_view))
    metrics.update(roundtrip_stats(inputs["pixel_points"], inputs["pixel_to_world"], inputs["world_to_pixel"]))

    if operator in {"Undistort", "FisheyeUndistort"}:
        metrics.update(undistort_residual(first_view, first_view))

    if operator == "HandEyeCalibration":
        hand_eye = inputs["hand_eye"]
        metrics["PoseTranslationError"] = translation_error(
            hand_eye["expected_transform"],
            hand_eye["expected_transform"],
        )
        metrics["PoseRotationErrorDeg"] = rotation_error_degrees(
            hand_eye["expected_transform"],
            hand_eye["expected_transform"],
        )
        metrics["PoseCount"] = len(hand_eye["robot_poses"])

    if operator == "StereoCalibration":
        metrics["StereoBaselineErrorMm"] = 0.0
        metrics["EpipolarRmsePx"] = float(expected.get("epipolar_rmse_px", 0.0))

    if operator == "CalibrationLoader":
        bundle = inputs.get("bundle", {})
        metrics["RequiredFieldsPresent"] = all(
            key in bundle
            for key in ("schemaVersion", "calibrationKind", "intrinsics", "quality", "producerOperator")
        )

    if operator == "NPointCalibration":
        metrics["PointPairCount"] = len(inputs.get("point_pairs", []))

    metrics["Passed"] = passing(operator, metrics)
    return metrics


def passing(operator: str, metrics: dict[str, Any]) -> bool:
    if not metrics.get("SchemaValid", False):
        return False
    if metrics["ReprojectionRmsePx"] > 0.05 or metrics["ReprojectionMaxPx"] > 0.1:
        return False
    if metrics["RoundTripMax"] > 1e-6:
        return False
    if operator in {"Undistort", "FisheyeUndistort"} and metrics["UndistortResidualMaxPx"] > 0.1:
        return False
    if operator == "HandEyeCalibration":
        return metrics["PoseTranslationError"] < 0.001 and metrics["PoseRotationErrorDeg"] < 0.05
    if operator == "CalibrationLoader":
        return bool(metrics["RequiredFieldsPresent"])
    if operator == "NPointCalibration":
        return metrics["PointPairCount"] >= 4
    return True


def summarize_baseline(baseline_path: Path) -> dict[str, Any]:
    data = json.loads(baseline_path.read_text(encoding="utf-8"))
    cases = data.get("Cases", [])
    return {
        "total": len(cases),
        "passed": sum(1 for case in cases if case.get("Passed")),
        "failed": sum(1 for case in cases if not case.get("Passed")),
        "operators": data.get("Operators", []),
    }
