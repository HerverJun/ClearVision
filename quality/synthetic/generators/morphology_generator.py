#!/usr/bin/env python3
"""Generate synthetic golden cases for Region morphology operators.

The cases are dependency-free JSON assets with run-length encoded input and
expected regions. They cover empty regions, single pixels, edge-touching masks,
multi-component masks, holes, thin structures, and kernel variations.
"""

from __future__ import annotations

import argparse
import json
import math
import random
from collections import deque
from pathlib import Path
from typing import Iterable


Point = tuple[int, int]
Run = dict[str, int]

OPERATORS = (
    "RegionErosion",
    "RegionDilation",
    "RegionOpening",
    "RegionClosing",
    "RegionSkeleton",
)

SCENARIOS = (
    "empty_region",
    "single_pixel",
    "edge_touching",
    "thin_horizontal",
    "thin_vertical",
    "multi_component",
    "inner_hole",
    "bridge_gap",
    "cross_shape",
    "large_mask",
    "tiny_roi",
)


def rect(x: int, y: int, width: int, height: int) -> set[Point]:
    return {
        (px, py)
        for py in range(y, y + height)
        for px in range(x, x + width)
    }


def to_runs(points: Iterable[Point]) -> list[Run]:
    by_row: dict[int, list[int]] = {}
    for x, y in points:
        by_row.setdefault(y, []).append(x)

    runs: list[Run] = []
    for y in sorted(by_row):
        xs = sorted(set(by_row[y]))
        if not xs:
            continue

        start = prev = xs[0]
        for x in xs[1:]:
            if x != prev + 1:
                runs.append({"y": y, "start_x": start, "end_x": prev})
                start = x
            prev = x
        runs.append({"y": y, "start_x": start, "end_x": prev})

    return runs


def bbox(points: set[Point]) -> list[int]:
    if not points:
        return [0, 0, 0, 0]

    xs = [x for x, _ in points]
    ys = [y for _, y in points]
    return [min(xs), min(ys), max(xs) - min(xs) + 1, max(ys) - min(ys) + 1]


def component_count(points: set[Point], connectivity: int = 8) -> int:
    if not points:
        return 0

    offsets = (
        ((0, -1), (-1, 0), (1, 0), (0, 1))
        if connectivity == 4
        else (
            (-1, -1),
            (0, -1),
            (1, -1),
            (-1, 0),
            (1, 0),
            (-1, 1),
            (0, 1),
            (1, 1),
        )
    )

    remaining = set(points)
    components = 0
    while remaining:
        seed = remaining.pop()
        queue: deque[Point] = deque([seed])
        components += 1
        while queue:
            x, y = queue.popleft()
            for dx, dy in offsets:
                neighbor = (x + dx, y + dy)
                if neighbor in remaining:
                    remaining.remove(neighbor)
                    queue.append(neighbor)
    return components


def endpoint_branch_counts(points: set[Point]) -> tuple[int, int]:
    if not points:
        return 0, 0

    offsets = (
        (-1, -1),
        (0, -1),
        (1, -1),
        (-1, 0),
        (1, 0),
        (-1, 1),
        (0, 1),
        (1, 1),
    )
    endpoints = 0
    branches = 0
    for x, y in points:
        neighbor_count = sum((x + dx, y + dy) in points for dx, dy in offsets)
        if neighbor_count == 1:
            endpoints += 1
        elif neighbor_count >= 3:
            branches += 1
    return endpoints, branches


def summarize(points: set[Point], operator: str) -> dict[str, object]:
    summary: dict[str, object] = {
        "area": len(points),
        "component_count": component_count(points),
        "bbox": bbox(points),
        "is_empty": not points,
        "connectivity": 8,
        "runs": to_runs(points),
    }
    if operator == "RegionSkeleton":
        endpoints, branches = endpoint_branch_counts(points)
        summary["skeleton_length"] = len(points)
        summary["end_points"] = endpoints
        summary["branch_points"] = branches
        summary["algorithm"] = "Zhang-Suen"
    return summary


def kernel_offsets(shape: str, width: int, height: int) -> list[Point]:
    width = max(1, width)
    height = max(1, height)
    anchor_x = (width - 1) // 2
    anchor_y = (height - 1) // 2
    offsets: list[Point] = []

    for ky in range(height):
        for kx in range(width):
            include = False
            if shape == "Cross":
                include = kx == anchor_x or ky == anchor_y
            elif shape == "Ellipse":
                row_start, row_end = opencv_ellipse_row_bounds(ky, width, height)
                include = row_start <= kx < row_end
            else:
                include = True

            if include:
                offsets.append((kx - anchor_x, ky - anchor_y))

    return offsets or [(0, 0)]


def opencv_ellipse_row_bounds(row: int, width: int, height: int) -> tuple[int, int]:
    """Match OpenCV's MORPH_ELLIPSE rasterization for odd/even kernels."""
    radius_y = height // 2
    center_x = width // 2
    if radius_y == 0:
        return 0, width

    dy = row - radius_y
    if abs(dy) > radius_y:
        return width, width

    radius_x = center_x
    dx = cv_round(radius_x * math.sqrt(max(0.0, (radius_y * radius_y - dy * dy) / (radius_y * radius_y))))
    return max(center_x - dx, 0), min(center_x + dx + 1, width)


def cv_round(value: float) -> int:
    return int(math.floor(value + 0.5))


def erode(points: set[Point], offsets: list[Point]) -> set[Point]:
    if not points:
        return set()
    return {
        (x, y)
        for x, y in points
        if all((x + dx, y + dy) in points for dx, dy in offsets)
    }


def dilate(points: set[Point], offsets: list[Point]) -> set[Point]:
    expanded: set[Point] = set()
    for x, y in points:
        for dx, dy in offsets:
            expanded.add((x + dx, y + dy))
    return expanded


def zhang_suen(points: set[Point], max_iterations: int = 100) -> set[Point]:
    if not points:
        return set()

    current = set(points)
    neighbor_order = (
        (0, -1),
        (1, -1),
        (1, 0),
        (1, 1),
        (0, 1),
        (-1, 1),
        (-1, 0),
        (-1, -1),
    )

    def neighbors(x: int, y: int) -> list[int]:
        return [1 if (x + dx, y + dy) in current else 0 for dx, dy in neighbor_order]

    def transitions(values: list[int]) -> int:
        return sum(values[i] == 0 and values[(i + 1) % 8] == 1 for i in range(8))

    for _ in range(max_iterations):
        changed = False
        for step in (0, 1):
            to_remove: set[Point] = set()
            for x, y in list(current):
                p = neighbors(x, y)
                neighbor_count = sum(p)
                if neighbor_count < 2 or neighbor_count > 6 or transitions(p) != 1:
                    continue

                if step == 0:
                    protect = p[0] * p[2] * p[4] == 0 and p[2] * p[4] * p[6] == 0
                else:
                    protect = p[0] * p[2] * p[6] == 0 and p[0] * p[4] * p[6] == 0

                if protect:
                    to_remove.add((x, y))

            if to_remove:
                current -= to_remove
                changed = True

        if not changed:
            break

    return current


def apply_operator(operator: str, points: set[Point], kernel: dict[str, object]) -> set[Point]:
    if operator == "RegionSkeleton":
        return zhang_suen(points, int(kernel["max_iterations"]))

    offsets = kernel_offsets(str(kernel["shape"]), int(kernel["width"]), int(kernel["height"]))
    if operator == "RegionErosion":
        return erode(points, offsets)
    if operator == "RegionDilation":
        return dilate(points, offsets)
    if operator == "RegionOpening":
        return dilate(erode(points, offsets), offsets)
    if operator == "RegionClosing":
        return erode(dilate(points, offsets), offsets)
    raise ValueError(f"Unsupported operator: {operator}")


def make_region(scenario: str, index: int, rng: random.Random) -> tuple[int, int, set[Point]]:
    width = 48 + (index % 4) * 8
    height = 40 + (index % 3) * 6

    if scenario == "tiny_roi":
        return 8, 7, rect(2, 2, 3, 3)
    if scenario == "empty_region":
        return width, height, set()
    if scenario == "single_pixel":
        return width, height, {(width // 2, height // 2)}
    if scenario == "edge_touching":
        return width, height, rect(0, 0, 10, 8)
    if scenario == "thin_horizontal":
        y = 4 + rng.randint(0, 3)
        return width, height, {(x, y) for x in range(2, width - 2)}
    if scenario == "thin_vertical":
        x = 4 + rng.randint(0, 3)
        return width, height, {(x, y) for y in range(2, height - 2)}
    if scenario == "multi_component":
        return width, height, rect(4, 4, 8, 8) | rect(width - 14, height - 14, 9, 9)
    if scenario == "inner_hole":
        outer = rect(8, 8, 24, 20)
        hole = rect(16, 14, 8, 6)
        return width, height, outer - hole
    if scenario == "bridge_gap":
        return width, height, rect(8, 12, 8, 10) | rect(19, 12, 8, 10)
    if scenario == "cross_shape":
        return width, height, rect(width // 2 - 3, 8, 7, height - 16) | rect(8, height // 2 - 3, width - 16, 7)
    if scenario == "large_mask":
        return width, height, rect(4, 4, width - 8, height - 8)

    return width, height, rect(10, 10, 12, 12)


def make_kernel(operator: str, index: int) -> dict[str, object]:
    if operator == "RegionSkeleton":
        return {"max_iterations": 100}

    shapes = ("Rectangle", "Cross", "Ellipse")
    sizes = ((1, 1), (3, 3), (5, 3), (5, 5), (7, 3))
    width, height = sizes[index % len(sizes)]
    return {
        "shape": shapes[index % len(shapes)],
        "width": width,
        "height": height,
        "iterations": 1,
    }


def build_case(operator: str, scenario: str, index: int, rng: random.Random) -> dict[str, object]:
    width, height, region = make_region(scenario, index, rng)
    kernel = make_kernel(operator, index)
    expected = apply_operator(operator, region, kernel)
    case_id = f"{operator}_{scenario}_{index:04d}"

    return {
        "version": 1,
        "case_id": case_id,
        "task": "region_morphology",
        "operator": operator,
        "scenario": scenario,
        "width": width,
        "height": height,
        "inputs": {
            "region": {"runs": to_runs(region)},
            "kernel": kernel,
        },
        "expected": summarize(expected, operator),
        "metrics": [
            "AreaError",
            "ComponentCountError",
            "BBoxIoU",
            "MaskIoU",
            "EmptyRegionBehavior",
            "RuntimeMs",
            "MemoryAllocation",
        ],
    }


def write_case(output_root: Path, case: dict[str, object]) -> None:
    case_dir = output_root / str(case["operator"]) / str(case["case_id"])
    case_dir.mkdir(parents=True, exist_ok=True)

    input_payload = {
        key: case[key]
        for key in (
            "version",
            "case_id",
            "task",
            "operator",
            "scenario",
            "width",
            "height",
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


def generate(output_root: Path, count: int, seed: int) -> list[dict[str, object]]:
    rng = random.Random(seed)
    cases: list[dict[str, object]] = []
    per_operator = max(1, count // len(OPERATORS))

    for operator in OPERATORS:
        for i in range(per_operator):
            scenario = SCENARIOS[i % len(SCENARIOS)]
            case = build_case(operator, scenario, i, rng)
            write_case(output_root, case)
            cases.append(case)

    manifest = {
        "version": 1,
        "seed": seed,
        "case_count": len(cases),
        "operators": list(OPERATORS),
        "cases": [
            {
                "case_id": case["case_id"],
                "operator": case["operator"],
                "scenario": case["scenario"],
                "expected_area": case["expected"]["area"],
            }
            for case in cases
        ],
    }
    (output_root / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return cases


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate Region morphology golden cases.")
    parser.add_argument("--output", type=Path, default=Path("quality/synthetic/cases/morphology"))
    parser.add_argument("--count", type=int, default=500, help="Total cases; rounded down by operator.")
    parser.add_argument("--seed", type=int, default=4203)
    args = parser.parse_args()

    cases = generate(args.output, args.count, args.seed)
    print(f"Generated {len(cases)} morphology cases under {args.output}")


if __name__ == "__main__":
    main()
