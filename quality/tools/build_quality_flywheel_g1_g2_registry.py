from __future__ import annotations

import json
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
MATRIX_PATH = REPO_ROOT / "quality" / "evals" / "reports" / "operator_quality_matrix.md"
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
JSON_OUTPUT = REPORT_DIR / "QualityFlywheel_G1_G2_registry.json"
MD_OUTPUT = REPORT_DIR / "QualityFlywheel_G1_G2_registry.md"

P3_CORE18 = [
    "ArcCaliper",
    "RegionComplement",
    "RegionDifference",
    "RegionIntersection",
    "ImageDiff",
    "ImageSubtract",
    "AdaptiveThreshold",
    "EdgeDetection",
    "ContourDetection",
    "BlobAnalysis",
    "BlobLabeling",
    "LineMeasurement",
    "CircleMeasurement",
    "GeometricFitting",
    "PerspectiveTransform",
    "AffineTransform",
    "DistanceTransform",
    "WidthMeasurement",
]

P2_RESIDUAL_STRATEGY = {
    "CalibrationLoader": {
        "owner": "Calibration Evidence Agent",
        "runner": "P2CalibrationResidualRunner",
        "strategy": "Load valid CalibrationBundleV2 fixtures; assert missing path, invalid JSON, version mismatch, and unit metadata failures.",
    },
    "DetectionSequenceJudge": {
        "owner": "AI/Rule Contract Agent",
        "runner": "P2InspectionResidualRunner",
        "strategy": "Replay deterministic sequence windows; assert order, threshold, missing stream, timeout, and structured reject contracts.",
    },
    "LocalDeformableMatching": {
        "owner": "Matching Evidence Agent",
        "runner": "P2MatchingResidualRunner",
        "strategy": "Use seeded template/scene pairs with local warp; assert pose, score, blank template, low texture, ROI, and deformation bounds.",
    },
    "NPointCalibration": {
        "owner": "Calibration Evidence Agent",
        "runner": "P2CalibrationResidualRunner",
        "strategy": "Use synthetic point correspondences with known affine/projective mapping; assert round-trip error and degenerate point failures.",
    },
    "PlanarMatching": {
        "owner": "Matching Evidence Agent",
        "runner": "P2MatchingResidualRunner",
        "strategy": "Use seeded homography scenes; assert inlier count, reprojection error, blank scene, insufficient features, and mask/ROI behavior.",
    },
    "ShapeMatching": {
        "owner": "Matching Evidence Agent",
        "runner": "P2MatchingResidualRunner",
        "strategy": "Use seeded rotation/scale templates; assert pose, angle/scale tolerance, score floor, and no-match structured failure.",
    },
    "SurfaceDefectDetection": {
        "owner": "AI/Rule Contract Agent",
        "runner": "P2InspectionResidualRunner",
        "strategy": "Use synthetic clean/scratch/blob/pinhole surfaces; assert defect count, bounding boxes, score thresholds, and invalid parameter failures.",
    },
    "TranslationRotationCalibration": {
        "owner": "Calibration Evidence Agent",
        "runner": "P2CalibrationResidualRunner",
        "strategy": "Use synthetic rigid/similarity transforms; assert translation/rotation error, inverse mapping, insufficient pairs, and collinear failures.",
    },
}


@dataclass(frozen=True)
class MatrixRow:
    operator: str
    display_name: str
    category: str
    qscore: int
    level: str
    known_limitations: int
    has_golden: bool
    golden_cases: int
    has_public_dataset: bool
    has_field_dataset: bool
    has_benchmark: bool
    priority: str
    owner_agent: str
    next_action: str


def split_markdown_row(line: str) -> list[str]:
    value = line.strip().strip("|")
    cells: list[str] = []
    current: list[str] = []
    escaped = False

    for char in value:
        if char == "\\" and not escaped:
            escaped = True
            current.append(char)
            continue
        if char == "|" and not escaped:
            cells.append("".join(current).strip().replace("\\|", "|"))
            current = []
            continue
        current.append(char)
        escaped = False

    cells.append("".join(current).strip().replace("\\|", "|"))
    return cells


def parse_bool(value: str) -> bool:
    return value.strip().lower() == "yes"


def parse_int(value: str) -> int:
    try:
        return int(value.strip())
    except ValueError:
        return 0


def parse_matrix(path: Path) -> list[MatrixRow]:
    rows: list[MatrixRow] = []

    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if not line.startswith("| OperatorType."):
            continue

        cells = split_markdown_row(line)
        if len(cells) < 21:
            raise ValueError(f"Unexpected matrix row shape ({len(cells)} cells): {line[:120]}")

        rows.append(
            MatrixRow(
                operator=cells[0].split(".")[-1],
                display_name=cells[1],
                category=cells[2],
                qscore=parse_int(cells[3]),
                level=cells[4],
                known_limitations=parse_int(cells[11]),
                has_golden=parse_bool(cells[13]),
                golden_cases=parse_int(cells[14]),
                has_public_dataset=parse_bool(cells[15]),
                has_field_dataset=parse_bool(cells[16]),
                has_benchmark=parse_bool(cells[17]),
                priority=cells[18],
                owner_agent=cells[19],
                next_action=cells[20],
            )
        )

    if not rows:
        raise ValueError(f"No matrix rows found in {path}")

    return rows


def evidence_layer(row: MatrixRow) -> str:
    if row.has_field_dataset:
        return "field+dataset+golden"
    if row.has_public_dataset:
        return "dataset+golden"
    if row.has_golden:
        return "golden-or-contract"
    return "planned"


def build_registry(rows: list[MatrixRow]) -> dict:
    by_operator = {row.operator: row for row in rows}
    p2_operators = [row.operator for row in rows if row.priority == "P2"]
    core50 = p2_operators + P3_CORE18
    missing = [operator for operator in core50 if operator not in by_operator]
    if missing:
        raise ValueError(f"Core50 operators missing from matrix: {missing}")
    if len(p2_operators) != 32:
        raise ValueError(f"Expected 32 P2 operators, got {len(p2_operators)}")
    if len(core50) != 50:
        raise ValueError(f"Expected core50 length 50, got {len(core50)}")

    p2_without_golden = [
        row.operator for row in rows if row.priority == "P2" and not row.has_golden
    ]
    core_rows = [by_operator[operator] for operator in core50]
    all_with_signal = [row for row in rows if row.has_golden]
    core_with_signal = [row for row in core_rows if row.has_golden]
    visual20_candidates = [
        row.operator
        for row in rows
        if row.priority == "P2"
        and row.operator
        not in {
            "CalibrationLoader",
            "CameraCalibration",
            "CoordinateTransform",
            "FisheyeCalibration",
            "HandEyeCalibration",
            "HandEyeCalibrationValidator",
            "NPointCalibration",
            "PixelToWorldTransform",
            "StereoCalibration",
            "TranslationRotationCalibration",
            "Undistort",
            "FisheyeUndistort",
        }
    ][:20]

    return {
        "schemaVersion": "2026-04-27.g1-g2",
        "generatedAtUtc": datetime.now(timezone.utc).replace(microsecond=0).isoformat(),
        "sourceMatrix": str(MATRIX_PATH.relative_to(REPO_ROOT)).replace("\\", "/"),
        "scopeNote": "Current matrix has HasGoldenTest only; G1 treats existing golden/contract baselines as contract evidence until HasContractTest is split out.",
        "baseline": {
            "totalOperators": len(rows),
            "levelCounts": dict(sorted({level: sum(1 for row in rows if row.level == level) for level in {row.level for row in rows}}.items())),
            "priorityCounts": dict(sorted({priority: sum(1 for row in rows if row.priority == priority) for priority in {row.priority for row in rows}}.items())),
            "goldenOrContractSignalYes": len(all_with_signal),
            "goldenOrContractSignalNo": len(rows) - len(all_with_signal),
            "p2WithoutGoldenEvidence": len(p2_without_golden),
            "publicOrAlternativeDatasetYes": sum(1 for row in rows if row.has_public_dataset),
            "fieldDatasetYes": sum(1 for row in rows if row.has_field_dataset),
        },
        "evidenceLayers": [
            {
                "id": "contract",
                "acceptance": "Happy path, missing input, parameter boundary, type/null boundary, and structured failure message.",
            },
            {
                "id": "golden",
                "acceptance": "Behavior, geometry, protocol, or synthetic oracle with at least 20 cases, 0 failures, runtime, and memory.",
            },
            {
                "id": "dataset",
                "acceptance": "Public dataset or licensed alternative/semi-synthetic tier with manifest, fixed version/seed, metrics, and failure boundaries.",
            },
            {
                "id": "field",
                "acceptance": "Anonymized failure sample with manifest, minimal replay, triage labels, and regression conversion status.",
            },
        ],
        "g1": {
            "target": "155/155 operators have basic contract evidence.",
            "currentContractOrGoldenSignal": len(all_with_signal),
            "remainingOperatorsWithoutSignal": len(rows) - len(all_with_signal),
            "status": "in-progress",
            "nextGate": "Split matrix into HasContractTest and HasGoldenTest, then expand P3 contract runners.",
        },
        "g2": {
            "target": "Core 50 operators have golden baselines.",
            "core50Frozen": True,
            "p2Included": len(p2_operators),
            "p3Included": len(P3_CORE18),
            "currentCore50GoldenOrContractSignal": len(core_with_signal),
            "remainingCore50WithoutGolden": len(core_rows) - len(core_with_signal),
            "status": "in-progress",
            "nextGate": "Implement P2 residual runners first, then cover the selected P3 vision chain.",
        },
        "p2ResidualGoldenPlan": [
            {"operator": operator, **P2_RESIDUAL_STRATEGY[operator]}
            for operator in p2_without_golden
        ],
        "visual20CandidatePool": visual20_candidates,
        "core50": [
            {
                "operator": row.operator,
                "priority": row.priority,
                "qscore": row.qscore,
                "level": row.level,
                "knownLimitations": row.known_limitations,
                "hasGolden": row.has_golden,
                "cases": row.golden_cases,
                "hasPublicDataset": row.has_public_dataset,
                "hasBenchmark": row.has_benchmark,
                "evidenceLayer": evidence_layer(row),
                "owner": (
                    P2_RESIDUAL_STRATEGY[row.operator]["owner"]
                    if row.operator in P2_RESIDUAL_STRATEGY
                    else row.owner_agent
                ),
                "nextAction": (
                    P2_RESIDUAL_STRATEGY[row.operator]["strategy"]
                    if row.operator in P2_RESIDUAL_STRATEGY
                    else row.next_action
                ),
            }
            for row in core_rows
        ],
        "allOperators": [
            {
                "operator": row.operator,
                "priority": row.priority,
                "level": row.level,
                "hasGolden": row.has_golden,
                "cases": row.golden_cases,
                "evidenceLayer": evidence_layer(row),
                "nextAction": row.next_action,
            }
            for row in rows
        ],
    }


def md_bool(value: bool) -> str:
    return "Yes" if value else "No"


def write_markdown(registry: dict) -> None:
    lines: list[str] = []
    baseline = registry["baseline"]
    g1 = registry["g1"]
    g2 = registry["g2"]

    lines.extend(
        [
            "# Quality Flywheel G1/G2 Registry",
            "",
            f"GeneratedAtUtc: `{registry['generatedAtUtc']}`",
            f"SourceMatrix: `{registry['sourceMatrix']}`",
            "",
            "## Scope",
            "",
            registry["scopeNote"],
            "",
            "## Status",
            "",
            f"- G1 current signal: {g1['currentContractOrGoldenSignal']}/{baseline['totalOperators']} operators.",
            f"- G1 remaining without signal: {g1['remainingOperatorsWithoutSignal']}.",
            f"- G2 Core50 frozen: {g2['core50Frozen']} ({g2['p2Included']} P2 + {g2['p3Included']} P3).",
            f"- G2 current Core50 signal: {g2['currentCore50GoldenOrContractSignal']}/50.",
            f"- G2 remaining Core50 without golden signal: {g2['remainingCore50WithoutGolden']}.",
            f"- P2 without golden evidence: {baseline['p2WithoutGoldenEvidence']}.",
            "",
            "## Evidence Layers",
            "",
            "| Layer | Acceptance |",
            "|---|---|",
        ]
    )

    for layer in registry["evidenceLayers"]:
        lines.append(f"| {layer['id']} | {layer['acceptance']} |")

    lines.extend(
        [
            "",
            "## P2 Residual Golden Plan",
            "",
            "| Operator | Owner | Runner | Strategy |",
            "|---|---|---|---|",
        ]
    )
    if registry["p2ResidualGoldenPlan"]:
        for item in registry["p2ResidualGoldenPlan"]:
            lines.append(
                f"| {item['operator']} | {item['owner']} | {item['runner']} | {item['strategy']} |"
            )
    else:
        lines.append("| None | - | - | P2 golden residual is closed. |")

    lines.extend(
        [
            "",
            "## Frozen Core 50",
            "",
            "| # | Operator | Priority | Has Golden | Cases | Evidence Layer | Owner |",
            "|---:|---|---|---|---:|---|---|",
        ]
    )
    for index, item in enumerate(registry["core50"], start=1):
        lines.append(
            f"| {index} | {item['operator']} | {item['priority']} | {md_bool(item['hasGolden'])} | {item['cases']} | {item['evidenceLayer']} | {item['owner']} |"
        )

    lines.extend(
        [
            "",
            "## Visual 20 Candidate Pool",
            "",
            "This is only a G3 candidate pool. It is recorded here so G2 selection does not drift into dataset-tier work.",
            "",
            "```text",
            "\n".join(registry["visual20CandidatePool"]),
            "```",
            "",
        ]
    )

    MD_OUTPUT.write_text("\n".join(lines), encoding="utf-8")


def main() -> None:
    rows = parse_matrix(MATRIX_PATH)
    registry = build_registry(rows)
    JSON_OUTPUT.write_text(
        json.dumps(registry, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    write_markdown(registry)
    print(f"Wrote {JSON_OUTPUT.relative_to(REPO_ROOT)}")
    print(f"Wrote {MD_OUTPUT.relative_to(REPO_ROOT)}")


if __name__ == "__main__":
    main()
