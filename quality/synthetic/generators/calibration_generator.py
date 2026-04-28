#!/usr/bin/env python3
"""Generate synthetic golden cases for calibration operators.

The generated cases are intentionally JSON-only. They describe calibration
geometry, camera intrinsics, distortion, planar transforms, stereo baselines,
and hand-eye pose bundles without checking in large rendered images.
"""

from __future__ import annotations

import argparse
import json
import math
import random
from pathlib import Path
from typing import Any


OPERATORS = (
    "CameraCalibration",
    "PixelToWorldTransform",
    "HandEyeCalibration",
    "Undistort",
    "FisheyeUndistort",
    "StereoCalibration",
    "NPointCalibration",
    "CoordinateTransform",
    "CalibrationLoader",
)

SCENARIOS = (
    "nominal_grid",
    "wide_angle",
    "tilted_board",
    "edge_coverage",
    "mild_distortion",
    "strong_distortion",
    "planar_roundtrip",
    "pose_bundle",
)


def chessboard_points(cols: int, rows: int, square_mm: float) -> list[list[float]]:
    return [[c * square_mm, r * square_mm, 0.0] for r in range(rows) for c in range(cols)]


def rotation_matrix(rx_deg: float, ry_deg: float, rz_deg: float) -> list[list[float]]:
    rx = math.radians(rx_deg)
    ry = math.radians(ry_deg)
    rz = math.radians(rz_deg)
    cx, sx = math.cos(rx), math.sin(rx)
    cy, sy = math.cos(ry), math.sin(ry)
    cz, sz = math.cos(rz), math.sin(rz)

    return [
        [cz * cy, cz * sy * sx - sz * cx, cz * sy * cx + sz * sx],
        [sz * cy, sz * sy * sx + cz * cx, sz * sy * cx - cz * sx],
        [-sy, cy * sx, cy * cx],
    ]


def transform_matrix(
    tx: float,
    ty: float,
    tz: float,
    rx_deg: float,
    ry_deg: float,
    rz_deg: float,
) -> list[list[float]]:
    rot = rotation_matrix(rx_deg, ry_deg, rz_deg)
    return [
        [rot[0][0], rot[0][1], rot[0][2], tx],
        [rot[1][0], rot[1][1], rot[1][2], ty],
        [rot[2][0], rot[2][1], rot[2][2], tz],
        [0.0, 0.0, 0.0, 1.0],
    ]


def matmul4(a: list[list[float]], b: list[list[float]]) -> list[list[float]]:
    return [[sum(a[r][k] * b[k][c] for k in range(4)) for c in range(4)] for r in range(4)]


def invert_rigid(transform: list[list[float]]) -> list[list[float]]:
    rot_t = [[transform[c][r] for c in range(3)] for r in range(3)]
    t = [transform[r][3] for r in range(3)]
    inv_t = [-sum(rot_t[r][c] * t[c] for c in range(3)) for r in range(3)]
    return [
        [rot_t[0][0], rot_t[0][1], rot_t[0][2], inv_t[0]],
        [rot_t[1][0], rot_t[1][1], rot_t[1][2], inv_t[1]],
        [rot_t[2][0], rot_t[2][1], rot_t[2][2], inv_t[2]],
        [0.0, 0.0, 0.0, 1.0],
    ]


def project_point(
    point: list[float],
    camera_matrix: list[list[float]],
    distortion: list[float],
    pose: list[list[float]],
) -> list[float]:
    x = pose[0][0] * point[0] + pose[0][1] * point[1] + pose[0][2] * point[2] + pose[0][3]
    y = pose[1][0] * point[0] + pose[1][1] * point[1] + pose[1][2] * point[2] + pose[1][3]
    z = pose[2][0] * point[0] + pose[2][1] * point[1] + pose[2][2] * point[2] + pose[2][3]
    xn = x / z
    yn = y / z
    r2 = xn * xn + yn * yn
    k1, k2, p1, p2, k3 = (distortion + [0.0] * 5)[:5]
    radial = 1.0 + k1 * r2 + k2 * r2 * r2 + k3 * r2 * r2 * r2
    xd = xn * radial + 2.0 * p1 * xn * yn + p2 * (r2 + 2.0 * xn * xn)
    yd = yn * radial + p1 * (r2 + 2.0 * yn * yn) + 2.0 * p2 * xn * yn
    return [
        camera_matrix[0][0] * xd + camera_matrix[0][2],
        camera_matrix[1][1] * yd + camera_matrix[1][2],
    ]


def base_intrinsics(index: int, scenario: str) -> dict[str, Any]:
    width = 1280 if scenario in {"wide_angle", "edge_coverage"} else 960
    height = 960 if scenario in {"wide_angle", "edge_coverage"} else 720
    focal = 620.0 if scenario == "wide_angle" else 780.0 + (index % 4) * 18.0
    return {
        "image_size": {"width": width, "height": height},
        "camera_matrix": [
            [focal, 0.0, width / 2.0 + (index % 3 - 1) * 3.0],
            [0.0, focal * 1.01, height / 2.0 + (index % 5 - 2) * 2.0],
            [0.0, 0.0, 1.0],
        ],
        "distortion": distortion_coefficients(index, scenario),
    }


def distortion_coefficients(index: int, scenario: str) -> list[float]:
    if scenario == "strong_distortion":
        return [0.18, -0.075, 0.0015, -0.001, 0.012]
    if scenario == "wide_angle":
        return [0.11, -0.04, 0.001, -0.0008, 0.006]
    if scenario == "mild_distortion":
        return [0.045, -0.014, 0.0004, -0.0003, 0.001]
    return [0.02 + index * 0.0005, -0.006, 0.0002, -0.0001, 0.0]


def board_pose(index: int, scenario: str) -> list[list[float]]:
    tilt = 18.0 if scenario in {"tilted_board", "edge_coverage"} else 6.0
    return transform_matrix(
        -90.0 + (index % 5) * 18.0,
        -60.0 + (index % 4) * 16.0,
        760.0 + (index % 6) * 24.0,
        tilt + (index % 3) * 2.0,
        -tilt / 2.0 + (index % 4) * 1.5,
        -12.0 + (index % 7) * 4.0,
    )


def planar_transform(index: int) -> list[list[float]]:
    scale_x = 0.042 + (index % 4) * 0.002
    scale_y = 0.044 + (index % 3) * 0.0015
    angle = math.radians(-7.0 + (index % 5) * 3.5)
    c = math.cos(angle)
    s = math.sin(angle)
    return [
        [scale_x * c, -scale_y * s, 12.0 + index * 0.1],
        [scale_x * s, scale_y * c, -8.0 + index * 0.05],
        [0.0, 0.0, 1.0],
    ]


def apply_homography(matrix: list[list[float]], point: list[float]) -> list[float]:
    x, y = point
    den = matrix[2][0] * x + matrix[2][1] * y + matrix[2][2]
    return [
        (matrix[0][0] * x + matrix[0][1] * y + matrix[0][2]) / den,
        (matrix[1][0] * x + matrix[1][1] * y + matrix[1][2]) / den,
    ]


def invert_homography_2d(matrix: list[list[float]]) -> list[list[float]]:
    a, b, c = matrix[0]
    d, e, f = matrix[1]
    g, h, i = matrix[2]
    det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g)
    if abs(det) < 1e-12:
        raise ValueError("singular homography")
    return [
        [(e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det],
        [(f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det],
        [(d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det],
    ]


def hand_eye_bundle(index: int, calibration_type: str) -> dict[str, Any]:
    if calibration_type == "eye_to_hand":
        expected = transform_matrix(-0.220, 0.080, 0.550, -2.0, 11.0, 18.0)
        target_to_tool = transform_matrix(0.012, -0.018, 0.040, 4.0, -3.0, 7.0)
        base_to_camera = invert_rigid(expected)
    else:
        expected = transform_matrix(0.030, -0.015, 0.080, 5.0, -8.0, 12.0)
        target_to_base = transform_matrix(0.450, 0.120, 0.250, 0.0, 0.0, 0.0)
        inv_camera_to_tool = invert_rigid(expected)

    robot_poses: list[list[list[float]]] = []
    board_poses: list[list[list[float]]] = []
    for sample in range(9):
        base_to_tool = transform_matrix(
            0.10 + sample * 0.012 + index * 0.0005,
            -0.05 + (sample % 4) * 0.035,
            0.30 + (sample % 5) * 0.018,
            -10.0 + sample * 2.8,
            5.0 - sample * 1.7,
            -16.0 + sample * 4.2,
        )
        if calibration_type == "eye_to_hand":
            tool_to_base = invert_rigid(base_to_tool)
            target_to_camera = matmul4(matmul4(target_to_tool, tool_to_base), base_to_camera)
        else:
            target_to_camera = matmul4(matmul4(target_to_base, base_to_tool), inv_camera_to_tool)
        robot_poses.append(base_to_tool)
        board_poses.append(invert_rigid(target_to_camera))

    return {
        "calibration_type": calibration_type,
        "expected_transform": expected,
        "robot_poses": robot_poses,
        "calibration_board_poses": board_poses,
    }


def build_case(operator: str, scenario: str, index: int, rng: random.Random) -> dict[str, Any]:
    intrinsics = base_intrinsics(index, scenario)
    pattern = {
        "type": "chessboard",
        "cols": 9,
        "rows": 6,
        "square_mm": 20.0 if scenario != "wide_angle" else 16.0,
    }
    object_points = chessboard_points(pattern["cols"], pattern["rows"], pattern["square_mm"])
    poses = [board_pose(index + view, scenario) for view in range(6)]
    image_points = [
        [project_point(point, intrinsics["camera_matrix"], intrinsics["distortion"], pose) for point in object_points]
        for pose in poses
    ]
    homography = planar_transform(index)
    inv_homography = invert_homography_2d(homography)
    pixel_points = [
        [120.0 + rng.random() * 620.0, 90.0 + rng.random() * 480.0]
        for _ in range(8)
    ]
    world_points = [apply_homography(homography, point) for point in pixel_points]
    stereo_right_pose = transform_matrix(-85.0, 0.0, 0.0, 0.0, -1.2, 0.1)
    calibration_type = "eye_to_hand" if index % 2 else "eye_in_hand"

    expected: dict[str, Any] = {
        "reprojection_error_px": 0.0,
        "max_reprojection_error_px": 0.0,
        "pose_translation_error": 0.0,
        "pose_rotation_error_deg": 0.0,
        "roundtrip_error": 0.0,
        "undistort_residual_px": 0.0,
        "accepted": True,
    }

    inputs: dict[str, Any] = {
        "intrinsics": intrinsics,
        "pattern": pattern,
        "object_points": object_points,
        "board_poses": poses,
        "image_points": image_points,
        "pixel_points": pixel_points,
        "world_points": world_points,
        "pixel_to_world": homography,
        "world_to_pixel": inv_homography,
    }

    if operator == "HandEyeCalibration":
        inputs["hand_eye"] = hand_eye_bundle(index, calibration_type)
        expected["matrix_convention"] = "CameraToBaseMatrix" if calibration_type == "eye_to_hand" else "CameraToToolMatrix"
    elif operator == "StereoCalibration":
        inputs["right_camera_pose"] = stereo_right_pose
        expected["baseline_mm"] = 85.0
        expected["epipolar_rmse_px"] = 0.0
    elif operator == "CalibrationLoader":
        inputs["bundle"] = calibration_bundle(operator, intrinsics, homography, index)
        expected["schema_valid"] = True
        expected["required_fields_present"] = True
    elif operator in {"Undistort", "FisheyeUndistort"}:
        expected["undistort_residual_px"] = 0.0
        inputs["distortion_model"] = "kannalaBrandt" if operator == "FisheyeUndistort" else "brownConrady"
    elif operator == "CameraCalibration":
        expected["estimated_intrinsics"] = intrinsics["camera_matrix"]
        expected["estimated_distortion"] = intrinsics["distortion"]
    elif operator == "NPointCalibration":
        inputs["point_pairs"] = [
            {"image": pixel, "world": world}
            for pixel, world in zip(pixel_points[:6], world_points[:6])
        ]
    elif operator == "CoordinateTransform":
        inputs["source_frame"] = "image"
        inputs["target_frame"] = "world"
    elif operator == "PixelToWorldTransform":
        inputs["transform_mode"] = "PixelToWorld"

    return {
        "case_id": f"{operator}_{scenario}_{index:04d}",
        "operator": operator,
        "scenario": scenario,
        "inputs": inputs,
        "expected": expected,
    }


def calibration_bundle(
    operator: str,
    intrinsics: dict[str, Any],
    homography: list[list[float]],
    index: int,
) -> dict[str, Any]:
    return {
        "schemaVersion": 2,
        "calibrationKind": "cameraIntrinsics",
        "transformModel": "planarHomography",
        "sourceFrame": "image",
        "targetFrame": "world",
        "unit": "mm",
        "imageSize": intrinsics["image_size"],
        "intrinsics": {"cameraMatrix": intrinsics["camera_matrix"]},
        "distortion": {
            "model": "brownConrady",
            "coefficients": intrinsics["distortion"],
        },
        "transform2D": {
            "model": "homography",
            "matrix": homography,
        },
        "quality": {
            "accepted": True,
            "meanError": round(0.03 + index * 0.001, 4),
            "maxError": round(0.09 + index * 0.002, 4),
            "inlierCount": 48,
            "totalSampleCount": 48,
            "diagnostics": [],
        },
        "producerOperator": operator,
    }


def generate_cases(per_operator: int, seed: int) -> list[dict[str, Any]]:
    rng = random.Random(seed)
    cases: list[dict[str, Any]] = []
    for operator in OPERATORS:
        for index in range(per_operator):
            scenario = SCENARIOS[index % len(SCENARIOS)]
            cases.append(build_case(operator, scenario, index, rng))
    return cases


def write_cases(cases: list[dict[str, Any]], output: Path) -> None:
    for case in cases:
        case_dir = output / case["operator"] / case["case_id"]
        case_dir.mkdir(parents=True, exist_ok=True)
        input_payload = {key: value for key, value in case.items() if key != "expected"}
        expected_payload = {
            "case_id": case["case_id"],
            "operator": case["operator"],
            "scenario": case["scenario"],
            "expected": case["expected"],
        }
        (case_dir / "input.json").write_text(json.dumps(input_payload, indent=2), encoding="utf-8")
        (case_dir / "expected.json").write_text(json.dumps(expected_payload, indent=2), encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate synthetic calibration golden cases.")
    parser.add_argument("--output", type=Path, default=Path("quality/synthetic/cases/calibration"))
    parser.add_argument("--per-operator", type=int, default=24)
    parser.add_argument("--seed", type=int, default=4242)
    args = parser.parse_args()

    cases = generate_cases(args.per_operator, args.seed)
    write_cases(cases, args.output)
    print(f"Generated {len(cases)} calibration cases under {args.output}")


if __name__ == "__main__":
    main()
