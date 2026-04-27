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
    has_contract: bool
    contract_cases: int
    has_golden: bool
    golden_cases: int
    has_dataset: bool
    dataset_cases: int
    has_field: bool
    field_cases: int
    has_benchmark: bool
    priority: str
    owner_agent: str
    next_action: str

    @property
    def has_signal(self) -> bool:
        return self.has_contract or self.has_golden or self.has_dataset or self.has_field

    @property
    def signal_cases(self) -> int:
        return max(self.contract_cases, self.golden_cases, self.dataset_cases, self.field_cases)


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
        if len(cells) < 25:
            raise ValueError(f"Unexpected matrix row shape ({len(cells)} cells): {line[:120]}")

        rows.append(
            MatrixRow(
                operator=cells[0].split(".")[-1],
                display_name=cells[1],
                category=cells[2],
                qscore=parse_int(cells[3]),
                level=cells[4],
                known_limitations=parse_int(cells[11]),
                has_contract=parse_bool(cells[13]),
                contract_cases=parse_int(cells[14]),
                has_golden=parse_bool(cells[15]),
                golden_cases=parse_int(cells[16]),
                has_dataset=parse_bool(cells[17]),
                dataset_cases=parse_int(cells[18]),
                has_field=parse_bool(cells[19]),
                field_cases=parse_int(cells[20]),
                has_benchmark=parse_bool(cells[21]),
                priority=cells[22],
                owner_agent=cells[23],
                next_action=cells[24],
            )
        )

    if not rows:
        raise ValueError(f"No matrix rows found in {path}")

    return rows


def evidence_layer(row: MatrixRow) -> str:
    if row.has_field and row.has_dataset and row.has_golden:
        return "field+dataset+golden"
    if row.has_field:
        return "field"
    if row.has_dataset and row.has_golden:
        return "dataset+golden"
    if row.has_dataset:
        return "dataset"
    if row.has_contract and row.has_golden:
        return "contract+golden"
    if row.has_golden:
        return "golden"
    if row.has_contract:
        return "contract"
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

    p2_without_signal = [
        row.operator for row in rows if row.priority == "P2" and not row.has_signal
    ]
    core_rows = [by_operator[operator] for operator in core50]
    all_with_signal = [row for row in rows if row.has_signal]
    core_with_signal = [row for row in core_rows if row.has_signal]
    g1_remaining = len(rows) - len(all_with_signal)
    g2_remaining = len(core_rows) - len(core_with_signal)
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
        "schemaVersion": "2026-04-27.g1-g2.evidence-split",
        "generatedAtUtc": datetime.now(timezone.utc).replace(microsecond=0).isoformat(),
        "sourceMatrix": str(MATRIX_PATH.relative_to(REPO_ROOT)).replace("\\", "/"),
        "scopeNote": "Matrix evidence is split into HasContractTest, HasGoldenTest, HasDatasetEvidence, and HasFieldReplay. G1 counts any accepted evidence signal; G2 Core50 counts accepted contract/golden/dataset/field signal without drifting into new dataset runs.",
        "baseline": {
            "totalOperators": len(rows),
            "levelCounts": dict(sorted({level: sum(1 for row in rows if row.level == level) for level in {row.level for row in rows}}.items())),
            "priorityCounts": dict(sorted({priority: sum(1 for row in rows if row.priority == priority) for priority in {row.priority for row in rows}}.items())),
            "anyEvidenceSignalYes": len(all_with_signal),
            "anyEvidenceSignalNo": len(rows) - len(all_with_signal),
            "contractSignalYes": sum(1 for row in rows if row.has_contract),
            "goldenSignalYes": sum(1 for row in rows if row.has_golden),
            "datasetEvidenceYes": sum(1 for row in rows if row.has_dataset),
            "fieldReplayYes": sum(1 for row in rows if row.has_field),
            "p2WithoutEvidenceSignal": len(p2_without_signal),
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
            "target": "155/155 operators have accepted evidence signal across contract/golden/dataset/field layers.",
            "currentEvidenceSignal": len(all_with_signal),
            "remainingOperatorsWithoutSignal": g1_remaining,
            "status": "complete" if g1_remaining == 0 else "in-progress",
            "nextGate": (
                "Maintain contract evidence and move to G3 dataset-tier execution."
                if g1_remaining == 0
                else "Expand P3 contract runners while preserving the split evidence columns."
            ),
        },
        "g2": {
            "target": "Core 50 operators have accepted contract/golden/dataset evidence signals.",
            "core50Frozen": True,
            "p2Included": len(p2_operators),
            "p3Included": len(P3_CORE18),
            "currentCore50EvidenceSignal": len(core_with_signal),
            "remainingCore50WithoutEvidenceSignal": g2_remaining,
            "status": "complete" if g2_remaining == 0 else "in-progress",
            "nextGate": (
                "Keep Core50 baselines in regression and move next effort to G1 P3 contract expansion / G3 dataset-tier selection."
                if g2_remaining == 0
                else "Implement P2 residual runners first, then cover the selected P3 vision chain."
            ),
        },
        "p2ResidualGoldenPlan": [
            {"operator": operator, **P2_RESIDUAL_STRATEGY[operator]}
            for operator in p2_without_signal
        ],
        "visual20CandidatePool": visual20_candidates,
        "core50": [
            {
                "operator": row.operator,
                "priority": row.priority,
                "qscore": row.qscore,
                "level": row.level,
                "knownLimitations": row.known_limitations,
                "hasContract": row.has_contract,
                "contractCases": row.contract_cases,
                "hasGolden": row.has_golden,
                "goldenCases": row.golden_cases,
                "hasDatasetEvidence": row.has_dataset,
                "datasetCases": row.dataset_cases,
                "hasFieldReplay": row.has_field,
                "fieldReplayCases": row.field_cases,
                "cases": row.signal_cases,
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
                "hasContract": row.has_contract,
                "hasGolden": row.has_golden,
                "hasDatasetEvidence": row.has_dataset,
                "hasFieldReplay": row.has_field,
                "cases": row.signal_cases,
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
            f"- G1 current evidence signal: {g1['currentEvidenceSignal']}/{baseline['totalOperators']} operators.",
            f"- G1 remaining without signal: {g1['remainingOperatorsWithoutSignal']}.",
            f"- G1 status: {g1['status']}.",
            f"- G2 Core50 frozen: {g2['core50Frozen']} ({g2['p2Included']} P2 + {g2['p3Included']} P3).",
            f"- G2 current Core50 evidence signal: {g2['currentCore50EvidenceSignal']}/50.",
            f"- G2 remaining Core50 without evidence signal: {g2['remainingCore50WithoutEvidenceSignal']}.",
            f"- G2 status: {g2['status']}.",
            f"- P2 without evidence signal: {baseline['p2WithoutEvidenceSignal']}.",
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
            "| # | Operator | Priority | Contract | Golden | Dataset | Field | Cases | Evidence Layer | Owner |",
            "|---:|---|---|---|---|---|---|---:|---|---|",
        ]
    )
    for index, item in enumerate(registry["core50"], start=1):
        lines.append(
            f"| {index} | {item['operator']} | {item['priority']} | {md_bool(item['hasContract'])} | {md_bool(item['hasGolden'])} | {md_bool(item['hasDatasetEvidence'])} | {md_bool(item['hasFieldReplay'])} | {item['cases']} | {item['evidenceLayer']} | {item['owner']} |"
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
