#!/usr/bin/env python3
"""Generate synthetic golden cases for Region boolean operators.

The generator writes one folder per case:

    <output>/<operator>/<case_id>/input.json
    <output>/<operator>/<case_id>/expected.json

The JSON uses run-length encoded masks so cases stay compact and can be
consumed by .NET, Python, or review tools without image dependencies.
"""

from __future__ import annotations

import argparse
import json
import random
from collections import deque
from pathlib import Path
from typing import Iterable


Point = tuple[int, int]
Run = dict[str, int]

OPERATORS = (
    "RegionUnion",
    "RegionIntersection",
    "RegionDifference",
    "RegionComplement",
)

SCENARIOS = (
    "empty_region",
    "full_region",
    "single_pixel",
    "edge_touching",
    "multi_connected",
    "inner_hole",
    "thin_region",
    "crossing_regions",
    "contained_region",
    "disjoint_regions",
    "duplicate_input",
    "tiny_roi",
)


def rect(x: int, y: int, width: int, height: int) -> set[Point]:
    return {
        (px, py)
        for py in range(y, y + height)
        for px in range(x, x + width)
    }


def full(width: int, height: int) -> set[Point]:
    return rect(0, 0, width, height)


def clamp_region(points: Iterable[Point], width: int, height: int) -> set[Point]:
    return {
        (x, y)
        for x, y in points
        if 0 <= x < width and 0 <= y < height
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

    if connectivity == 4:
        offsets = ((0, -1), (-1, 0), (1, 0), (0, 1))
    else:
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


def summarize(points: set[Point], connectivity: int = 8) -> dict[str, object]:
    return {
        "area": len(points),
        "component_count": component_count(points, connectivity),
        "bbox": bbox(points),
        "is_empty": not points,
        "connectivity": connectivity,
        "runs": to_runs(points),
    }


def make_regions(scenario: str, index: int, rng: random.Random) -> tuple[int, int, set[Point], set[Point]]:
    width = 48 + (index % 4) * 8
    height = 40 + (index % 3) * 6

    if scenario == "tiny_roi":
        width, height = 8, 7

    jitter_x = rng.randint(0, max(0, width // 8))
    jitter_y = rng.randint(0, max(0, height // 8))

    if scenario == "empty_region":
        return width, height, set(), rect(5, 5, 9, 7)
    if scenario == "full_region":
        return width, height, full(width, height), rect(width // 3, height // 3, 7, 6)
    if scenario == "single_pixel":
        return width, height, {(width // 2, height // 2)}, {(width // 2 + 1, height // 2)}
    if scenario == "edge_touching":
        return width, height, rect(0, 0, 10, 8), rect(width - 8, height - 8, 8, 8)
    if scenario == "multi_connected":
        a = rect(4, 4, 8, 8) | rect(width - 14, height - 14, 9, 9)
        b = rect(8, 8, 12, 8)
        return width, height, a, b
    if scenario == "inner_hole":
        outer = rect(8, 8, 24, 20)
        hole = rect(16, 14, 8, 6)
        return width, height, outer - hole, rect(18, 15, 4, 3)
    if scenario == "thin_region":
        a = {(x, 6 + jitter_y) for x in range(2, width - 2)}
        b = {(width // 2, y) for y in range(2, height - 2)}
        return width, height, a, b
    if scenario == "crossing_regions":
        a = rect(width // 2 - 2, 4, 5, height - 8)
        b = rect(4, height // 2 - 2, width - 8, 5)
        return width, height, a, b
    if scenario == "contained_region":
        a = rect(6, 6, width - 12, height - 12)
        b = rect(12 + jitter_x, 12 + jitter_y, 7, 5)
        return width, height, a, b
    if scenario == "disjoint_regions":
        return width, height, rect(3, 3, 8, 8), rect(width - 13, height - 13, 8, 8)
    if scenario == "duplicate_input":
        a = rect(10, 10, 12, 9) | rect(25, 14, 6, 6)
        return width, height, a, set(a)

    a = rect(1, 1, 3, 3)
    b = rect(width - 4, height - 4, 3, 3)
    return width, height, a, b


def apply_operator(operator: str, width: int, height: int, region1: set[Point], region2: set[Point]) -> set[Point]:
    region1 = clamp_region(region1, width, height)
    region2 = clamp_region(region2, width, height)
    if operator == "RegionUnion":
        return region1 | region2
    if operator == "RegionIntersection":
        return region1 & region2
    if operator == "RegionDifference":
        return region1 - region2
    if operator == "RegionComplement":
        return full(width, height) - region1
    raise ValueError(f"Unsupported operator: {operator}")


def build_case(operator: str, scenario: str, index: int, rng: random.Random) -> dict[str, object]:
    width, height, region1, region2 = make_regions(scenario, index, rng)
    expected_region = apply_operator(operator, width, height, region1, region2)
    case_id = f"{operator}_{scenario}_{index:04d}"

    inputs: dict[str, object]
    if operator == "RegionComplement":
        inputs = {
            "region": {"runs": to_runs(region1)},
            "image_width": width,
            "image_height": height,
        }
    else:
        inputs = {
            "region1": {"runs": to_runs(region1)},
            "region2": {"runs": to_runs(region2)},
        }

    return {
        "version": 1,
        "case_id": case_id,
        "task": "region_operation",
        "operator": operator,
        "scenario": scenario,
        "width": width,
        "height": height,
        "inputs": inputs,
        "expected": summarize(expected_region),
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
    parser = argparse.ArgumentParser(description="Generate Region golden cases.")
    parser.add_argument("--output", type=Path, default=Path("quality/synthetic/cases/region"))
    parser.add_argument("--count", type=int, default=400, help="Total cases; rounded down by operator.")
    parser.add_argument("--seed", type=int, default=4202)
    args = parser.parse_args()

    cases = generate(args.output, args.count, args.seed)
    print(f"Generated {len(cases)} region cases under {args.output}")


if __name__ == "__main__":
    main()
