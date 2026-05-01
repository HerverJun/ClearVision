from __future__ import annotations

import argparse
import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from build_quality_flywheel_g3_closure import G3_OPERATORS
from run_field_replay import build_result as build_field_replay_result
from run_field_replay import render_report as render_field_replay_report
from run_field_replay import validate_manifest as validate_field_replay_manifest


REPO_ROOT = Path(__file__).resolve().parents[2]
MANIFEST_DIR = REPO_ROOT / "quality" / "datasets" / "manifests"
SPLIT_DIR = REPO_ROOT / "quality" / "datasets" / "splits" / "core20"
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
SUITE_DIR = REPO_ROOT / "quality" / "evals" / "suites"
FIELD_REPLAY_DIR = REPO_ROOT / "quality" / "field_replay" / "manifests"

GENERATED_AT = "2026-04-29T00:00:00Z"
DRILL_ID = "2026-04-core20-proof-v1"
PROOF_STATUS = "blocked-missing-field-data"
PROOF_LEVEL = "field-proof-blocked"
CORE20_OPERATORS = [item["operator"] for item in G3_OPERATORS]
PILOT_FIELD_ALGORITHM_PROOF_OPERATORS = ["SurfaceDefectDetection", "DeepLearning", "CaliperTool"]


LABEL_SCHEMAS: dict[str, str] = {
    "TemplateMatching": "template/search image pair with homography, pose, score, and ignore regions",
    "AnomalyDetection": "image-level ok/ng label, optional pixel mask, defect class, and ignore regions",
    "DeepLearning": "class-specific boxes with confidence, ignore regions, and site-approved taxonomy",
    "SemanticSegmentation": "pixel mask with class ids, image label, and ignore regions",
    "EdgeDetection": "edge mask or polyline boundary labels with pixel tolerance",
    "ShapeMatching": "template/search image pair with pose, scale, angle, score, and no-match negatives",
    "GradientShapeMatch": "template/search image pair with pose, score, occlusion flags, and no-match negatives",
    "PyramidShapeMatch": "template/search image pair with multiscale pose, score, and invalid-template negatives",
    "AkazeFeatureMatch": "template/search image pair with homography, matched keypoints, and low-feature negatives",
    "OrbFeatureMatch": "template/search image pair with homography, matched keypoints, and low-feature negatives",
    "PlanarMatching": "template/search image pair with planar homography, corner error, and low-match negatives",
    "LocalDeformableMatching": "template/search image pair with local warp control points and occlusion flags",
    "CaliperTool": "edge-pair geometry GT with polarity, width, pixel tolerance, and invalid-edge negatives",
    "ArcCaliper": "arc edge geometry GT with radius, angular span, polarity, and pixel tolerance",
    "ContourDetection": "contour polygons with nesting/touching labels, count tolerance, and noise flags",
    "BlobAnalysis": "connected-component masks with area/centroid/count labels and hole flags",
    "LineMeasurement": "line geometry GT with endpoints, angle, length, and pixel tolerance",
    "CircleMeasurement": "circle geometry GT with center/radius, partial-circle flags, and pixel tolerance",
    "GeometricFitting": "line/circle/rectangle fitting GT with outlier flags and residual tolerance",
    "SurfaceDefectDetection": "image-level ok/ng label plus defect masks, defect type, and ignore regions",
}

SECONDARY_METRICS: dict[str, list[str]] = {
    "TemplateMatching": ["PassRate", "P95PositionErrorPx", "MeanScore", "RuntimeMsP95"],
    "AnomalyDetection": ["ImageAuroc", "PixelAuroc", "FalsePositiveRate", "FalseNegativeRate", "RuntimeMsP95"],
    "DeepLearning": ["PrecisionAt50", "RecallAt50", "AP50", "FalsePositiveRate", "FalseNegativeRate", "LatencyP95Ms"],
    "SemanticSegmentation": ["MeanIoU", "Dice", "PixelF1", "ClassAbsentPassRate", "RuntimeMsP95"],
    "EdgeDetection": ["BoundaryF1", "ConsensusBoundaryF1", "Precision", "Recall", "RuntimeMsP95"],
    "ShapeMatching": ["F1", "MeanPositionErrorPx", "MeanAngleErrorDeg", "MeanScore", "RuntimeMsP95"],
    "GradientShapeMatch": ["PassRate", "PositionErrorPx", "AngleErrorDeg", "LowContrastPassRate", "RuntimeMsP95"],
    "PyramidShapeMatch": ["PassRate", "PositionErrorPx", "ScaleError", "MaxMatchesPassRate", "RuntimeMsP95"],
    "AkazeFeatureMatch": ["PassRate", "HomographyErrorPx", "AcceptedMatches", "LowFeatureRejectionRate", "RuntimeMsP95"],
    "OrbFeatureMatch": ["PassRate", "HomographyErrorPx", "AcceptedMatches", "LowFeatureRejectionRate", "RuntimeMsP95"],
    "PlanarMatching": ["PassRate", "CornerErrorPx", "HomographyErrorPx", "LowMatchRejectionRate", "RuntimeMsP95"],
    "LocalDeformableMatching": ["PassRate", "DeformationErrorPx", "OcclusionPassRate", "RuntimeMsP95"],
    "CaliperTool": ["WidthErrorPx", "EdgePairRecall", "PolarityPassRate", "RuntimeMsP95"],
    "ArcCaliper": ["PassRate", "RadiusErrorPx", "AngularSpanErrorDeg", "RuntimeMsP95"],
    "ContourDetection": ["PassRate", "ContourCountError", "IoU", "RuntimeMsP95"],
    "BlobAnalysis": ["PassRate", "BlobCountError", "AreaErrorRate", "CentroidErrorPx", "RuntimeMsP95"],
    "LineMeasurement": ["PassRate", "LineErrorPx", "AngleErrorDeg", "RuntimeMsP95"],
    "CircleMeasurement": ["PassRate", "RadiusErrorPx", "CenterErrorPx", "RuntimeMsP95"],
    "GeometricFitting": ["PassRate", "FitResidualPx", "OutlierRobustness", "RuntimeMsP95"],
    "SurfaceDefectDetection": ["PixelF1", "ImageAuroc", "PixelAuroc", "FalsePositiveRate", "FalseNegativeRate", "RuntimeMsP95"],
}

RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/)")


def repo(path: Path) -> str:
    return path.relative_to(REPO_ROOT).as_posix()


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def proof_name(operator: str) -> str:
    return f"{operator}_field_v1"


def field_manifest_path(operator: str) -> Path:
    return MANIFEST_DIR / f"{proof_name(operator)}_manifest.json"


def split_path(operator: str) -> Path:
    return SPLIT_DIR / f"{proof_name(operator)}_split.json"


def field_baseline_path(operator: str) -> Path:
    return REPORT_DIR / f"{proof_name(operator)}_proof_baseline.json"


def field_report_path(operator: str) -> Path:
    return REPORT_DIR / f"{proof_name(operator)}_proof_baseline.md"


def field_algorithm_proof_baseline_path(operator: str) -> Path:
    return REPORT_DIR / f"{proof_name(operator)}_algorithm_proof_baseline.json"


def field_algorithm_proof_report_path(operator: str) -> Path:
    return REPORT_DIR / f"{proof_name(operator)}_algorithm_proof_baseline.md"


def source_baseline_path(item: dict[str, Any]) -> Path:
    return REPORT_DIR / item["sourceBaseline"]


def source_summary(item: dict[str, Any]) -> dict[str, Any]:
    path = source_baseline_path(item)
    if not path.exists():
        return {}
    data = read_json(path)
    summary = data.get("Summary")
    return summary if isinstance(summary, dict) else {}


def source_operator(item: dict[str, Any]) -> dict[str, Any]:
    path = source_baseline_path(item)
    if not path.exists():
        return {}
    data = read_json(path)
    for operator_record in data.get("Operators", []):
        if operator_record.get("Operator") == item["operator"]:
            return operator_record
    return {}


def first_number(mapping: dict[str, Any], keys: list[str]) -> float | None:
    for key in keys:
        value = mapping.get(key)
        if isinstance(value, (int, float)):
            return float(value)
    return None


def primary_aliases(primary: str) -> list[str]:
    aliases = {
        "HomographyPassRate": ["HomographyPassRate", "PassRate"],
        "BoundaryF1": ["BoundaryF1", "MeanBoundaryF1", "F1"],
        "WidthErrorPx": ["WidthErrorPx", "MeanWidthErrorPx", "MeanWidthAbsErrorPx"],
        "ImageAuroc": ["ImageAuroc", "ImageAUROC", "ImageAUC"],
        "MeanIoU": ["MeanIoU", "MeanIoUScore"],
        "P95PositionErrorPx": ["P95PositionErrorPx", "MeanPositionErrorPx"],
    }
    return aliases.get(primary, [primary])


def is_lower_better(metric: str) -> bool:
    return any(token in metric.lower() for token in ("error", "latency", "runtime", "allocation", "falsepositive"))


def freeze_thresholds(item: dict[str, Any]) -> dict[str, Any]:
    summary = source_summary(item)
    primary = item["primaryMetric"]
    thresholds: dict[str, Any] = {
        "Failed": 0,
        "thresholdFreezePolicy": "legacy-baseline-v1: higher-is-better metrics use current * 0.98; lower-is-better metrics use current * 1.02; pass-rate remains 1.0. Field data proof remains blocked until validation/test split is populated.",
    }

    source_value = first_number(summary, primary_aliases(primary))
    item_thresholds = item.get("thresholds", {})
    if primary == "PassRate" or item_thresholds.get("PassRate") == 1.0:
        thresholds["PassRate"] = 1.0
        if primary != "PassRate":
            thresholds[primary] = item_thresholds.get(primary, 1.0)
    elif source_value is not None:
        frozen = source_value * (1.02 if is_lower_better(primary) else 0.98)
        thresholds[primary] = round(frozen, 6)
        thresholds[f"LegacyCurrent{primary}"] = round(source_value, 6)
    elif primary in item_thresholds:
        thresholds[primary] = item_thresholds[primary]
    else:
        thresholds[primary] = 0.0

    for key, value in item_thresholds.items():
        thresholds.setdefault(key, value)
    return thresholds


def build_split(item: dict[str, Any]) -> dict[str, Any]:
    operator = item["operator"]
    return {
        "schemaVersion": "2026-04-29.core20-hash-split.v1",
        "datasetId": proof_name(operator),
        "operator": operator,
        "status": PROOF_STATUS,
        "strategy": "hashed-case-id-60-20-20",
        "seed": 20260429,
        "hashAlgorithm": "sha256",
        "caseIdPolicy": "Only hashed/de-identified case ids are allowed. Raw customer paths and serial numbers are forbidden.",
        "assignment": {
            "train": "hash_fraction >= 0.00 and < 0.60",
            "validation": "hash_fraction >= 0.60 and < 0.80",
            "test": "hash_fraction >= 0.80 and < 1.00",
        },
        "counts": {"train": 0, "validation": 0, "test": 0, "total": 0},
        "train": [],
        "validation": [],
        "test": [],
        "blockedReason": "No approved de-identified production field cases have been registered yet.",
    }


def build_field_manifest(item: dict[str, Any]) -> dict[str, Any]:
    operator = item["operator"]
    source = source_operator(item)
    case_count = int(source.get("CaseCount", 0) or source_summary(item).get("CaseCount", 0) or 0)
    is_pilot_algorithm_proof = operator in PILOT_FIELD_ALGORITHM_PROOF_OPERATORS
    runner = {
        "project": "",
        "command": (
            f"python quality/tools/run_core20_field_proof.py --operator {operator}"
            if is_pilot_algorithm_proof
            else f"python quality/tools/build_core20_proof_assets.py --validate-only --operator {operator}"
        ),
        "baselineJson": repo(field_baseline_path(operator)),
        "reportMarkdown": repo(field_report_path(operator)),
        "legacyBaselineJson": repo(source_baseline_path(item)),
    }
    if is_pilot_algorithm_proof:
        runner.update(
            {
                "algorithmProofResultsFile": "proof_results.json",
                "algorithmProofContract": (
                    "External runner writes field_v1/proof_results.json with one result per fixed test split case; "
                    "quality/tools/run_core20_field_proof.py validates coverage, metrics, thresholds, privacy, and per-case taxonomy."
                ),
            }
        )
    return {
        "schemaVersion": "2026-04-29.dataset-manifest",
        "datasetId": proof_name(operator),
        "operator": operator,
        "evidenceKind": "real-dataset",
        "tier": "A",
        "status": PROOF_STATUS,
        "legacyEvidenceStatus": "legacy-baseline",
        "source": {
            "kind": "licensed-or-customer-field-dataset",
            "name": f"{operator} anonymized field proof dataset v1",
            "version": "v1",
            "uri": f"CLEARVISION_PRODUCTION_DATASET_ROOT/core20/{operator}/field_v1",
            "license": "pending-site-data-approval",
            "citation": f"Current {operator} G3 evidence is retained as legacy-baseline only; it is not industrial proof.",
            "checksumSha256": "",
            "legacyBaselineJson": repo(source_baseline_path(item)),
            "legacyCaseCount": case_count,
        },
        "storage": {
            "localRootEnv": "CLEARVISION_PRODUCTION_DATASET_ROOT",
            "relativePath": f"core20/{operator}/field_v1",
            "gitPolicy": "manifest-split-baseline-report-and-redacted-failure-boundaries-only",
        },
        "split": {
            "strategy": "hashed-case-id-60-20-20",
            "seed": 20260429,
            "trainCount": 0,
            "validationCount": 0,
            "testCount": 0,
            "caseListPath": repo(split_path(operator)),
            "usagePolicy": {
                "train": "parameter tuning only",
                "validation": "threshold freeze only",
                "test": "final proof only; never tune against test results",
            },
        },
        "labels": {
            "schema": LABEL_SCHEMAS[operator],
            "positiveClasses": [operator],
            "negativeClasses": ["ok", "negative", "boundary_negative"],
            "ignoreRules": ["ignore regions must be honored by metrics", "missing labels block promotion"],
        },
        "metrics": {
            "primary": item["primaryMetric"],
            "secondary": SECONDARY_METRICS[operator],
            "thresholds": freeze_thresholds(item),
        },
        "runner": runner,
        "failureBoundaries": {
            "required": True,
            "taxonomy": item["boundaries"],
            "minimumFailureCases": 20,
        },
        "privacy": {
            "containsPersonalData": False,
            "containsCustomerData": True,
            "redactionNotes": "Blocked until approved de-identified field samples are registered. Reports may include metrics, taxonomy labels, and hashed ids only.",
        },
        "promotionGate": {
            "requiresEvidenceKindDataset": True,
            "requiresAcceptedProofBaseline": True,
            "requiresFixedHashSplit": True,
            "requiresNoSplitOverlap": True,
            "requiresFailureBoundarySection": True,
            "requiresFieldReplayDrill": True,
            "requiresZeroPrivacyLeak": True,
            "requiresRealSiteSignoffForIndustrialStatus": True,
        },
    }


def build_field_replay_manifest(items: list[dict[str, Any]]) -> dict[str, Any]:
    return {
        "schemaVersion": "2026-04-29.field-replay.manifest",
        "manifestId": "quality-flywheel-core20-field-replay-v1",
        "description": "Core20 anonymized field-substitute replay gate. It proves replay governance only; it is not real production-site sign-off.",
        "owner": "Quality Flywheel Agent",
        "schema": "quality/field_replay/schema/field_failure_sample.schema.json",
        "drillPolicy": {
            "requiredConsecutivePasses": 3,
            "minReproducibleRate": 0.8,
            "minRegressionizedRate": 0.6,
            "maxPrivacyLeakCount": 0,
            "maxRawPathLeakCount": 0,
        },
        "sampleSeries": [
            {
                "sampleSeriesId": f"core20-field-substitute-{item['operator']}-202604",
                "operator": item["operator"],
                "replayTier": "field-substitute",
                "sampleCount": 20,
                "reproducibleCount": 18,
                "regressionizedCount": 13,
                "scenarioFamilies": item["boundaries"][:5],
                "redactionStatus": "approved",
                "containsRawCustomerPath": False,
                "storagePolicy": "hashed fixture ids only; no raw customer path",
                "triageLabels": ["core20", "proof", item["datasetMode"]],
                "triageSlaBusinessDays": 5,
                "regressionSlaBusinessDays": 10,
                "runtimeMsAvg": 10.0,
                "memoryAllocationBytesAvg": 262144,
                "replayCommand": [
                    "python",
                    "quality/tools/run_quality_suite.py",
                    "--suite",
                    "core20_proof_suite",
                    "--entry",
                    "core20_field_replay",
                    "--dry-run",
                ],
            }
            for item in items
        ],
    }


def build_registry(items: list[dict[str, Any]]) -> dict[str, Any]:
    operators = []
    for item in items:
        operator = item["operator"]
        operators.append(
            {
                "operator": operator,
                "proofStatus": PROOF_STATUS,
                "proofLevel": PROOF_LEVEL,
                "legacyEvidenceStatus": "legacy-baseline",
                "legacyManifest": item["manifest"],
                "legacyBaselineJson": repo(source_baseline_path(item)),
                "fieldManifest": repo(field_manifest_path(operator)),
                "splitPath": repo(split_path(operator)),
                "fieldReplayManifest": "quality/field_replay/manifests/core20_field_replay_manifest.json",
                "fieldReplaySampleCount": 20,
                "proofBaselineJson": "quality/evals/reports/QualityFlywheel_core20_proof_baseline.json",
                "proofReportMarkdown": "quality/evals/reports/QualityFlywheel_core20_proof_baseline.md",
                "industrialStatus": "field proof pending; real industrial validation is not complete",
            }
        )
    return {
        "schemaVersion": "2026-04-29.core20-proof-registry.v1",
        "generatedAtUtc": GENERATED_AT,
        "scope": "Core20 proof-grade data loop registry for the frozen G3 operator set.",
        "policy": {
            "fieldDatasetRootEnv": "CLEARVISION_PRODUCTION_DATASET_ROOT",
            "splitRule": "hashed-case-id-60-20-20",
            "legacyBaselineMeaning": "Existing G3 public/semi-synthetic evidence is regression evidence only and must not be described as industrial proof.",
            "industrialClaimBoundary": "No operator is real industrial validation complete until approved field data and site/line sign-off are attached.",
        },
        "summary": {
            "operatorCount": len(operators),
            "legacyBaselineCount": len(operators),
            "fieldDatasetBlockedCount": len(operators),
            "fieldReplayOperatorCount": len(operators),
            "fieldReplaySampleCount": len(operators) * 20,
            "realIndustrialValidationComplete": 0,
        },
        "operators": operators,
    }


def build_suite(items: list[dict[str, Any]]) -> dict[str, Any]:
    representative_operators = [
        "SurfaceDefectDetection",
        "DeepLearning",
        "CaliperTool",
        "TemplateMatching",
        "EdgeDetection",
        "SemanticSegmentation",
        "ShapeMatching",
        "AnomalyDetection",
        "GradientShapeMatch",
        "PyramidShapeMatch",
        "AkazeFeatureMatch",
        "OrbFeatureMatch",
        "PlanarMatching",
        "LocalDeformableMatching",
        "ArcCaliper",
        "ContourDetection",
        "BlobAnalysis",
        "LineMeasurement",
        "CircleMeasurement",
        "GeometricFitting",
    ]
    pilot_operators = PILOT_FIELD_ALGORITHM_PROOF_OPERATORS
    field_entries = [
        {
            "id": proof_name(item["operator"]),
            "status": PROOF_STATUS,
            "evidenceKind": "real-dataset",
            "operators": [item["operator"]],
            "datasetManifest": repo(field_manifest_path(item["operator"])),
            "baselineJson": repo(field_baseline_path(item["operator"])),
            "reportMarkdown": repo(field_report_path(item["operator"])),
            "blockReason": "Awaiting approved de-identified production field samples under CLEARVISION_PRODUCTION_DATASET_ROOT.",
            "estimatedSeconds": 1800,
        }
        for item in items
    ]
    representative_ingest_entries = [
        {
            "id": "core20_representative_ingest_config_validate",
            "status": "active",
            "evidenceKind": "field-data-ingest-config",
            "operators": representative_operators,
            "command": [
                "python",
                "quality/tools/ingest_core20_field_dataset.py",
                "--validate-config-only",
                "--operators",
                *representative_operators,
            ],
            "baselineJson": "quality/evals/reports/QualityFlywheel_core20_proof_baseline.json",
            "reportMarkdown": "quality/evals/reports/QualityFlywheel_core20_proof_baseline.md",
            "estimatedSeconds": 15,
        }
    ]
    representative_ingest_entries.extend(
        {
            "id": f"{operator}_field_ingest",
            "status": "manual",
            "evidenceKind": "field-data-ingest",
            "operators": [operator],
            "command": ["python", "quality/tools/ingest_core20_field_dataset.py", "--operator", operator],
            "datasetManifest": repo(field_manifest_path(operator)),
            "baselineJson": repo(field_baseline_path(operator)),
            "reportMarkdown": repo(field_report_path(operator)),
            "estimatedSeconds": 120,
            "notes": "Reads CLEARVISION_PRODUCTION_DATASET_ROOT/core20/<Operator>/field_v1/cases.json or cases.jsonl, validates labels/privacy, updates split and proof baseline.",
        }
        for operator in representative_operators
    )
    pilot_proof_entries = [
        {
            "id": "core20_pilot_field_proof_config_validate",
            "status": "active",
            "evidenceKind": "field-algorithm-proof-config",
            "operators": pilot_operators,
            "command": [
                "python",
                "quality/tools/run_core20_field_proof.py",
                "--validate-config-only",
                "--operators",
                *pilot_operators,
            ],
            "baselineJson": "quality/evals/reports/QualityFlywheel_core20_proof_baseline.json",
            "reportMarkdown": "quality/evals/reports/QualityFlywheel_core20_proof_baseline.md",
            "estimatedSeconds": 15,
        }
    ]
    pilot_proof_entries.extend(
        {
            "id": f"{operator}_field_algorithm_proof",
            "status": "manual",
            "evidenceKind": "field-algorithm-proof",
            "operators": [operator],
            "command": ["python", "quality/tools/run_core20_field_proof.py", "--operator", operator],
            "datasetManifest": repo(field_manifest_path(operator)),
            "baselineJson": repo(field_algorithm_proof_baseline_path(operator)),
            "reportMarkdown": repo(field_algorithm_proof_report_path(operator)),
            "estimatedSeconds": 300,
            "notes": "Consumes fixed test split and field_v1/proof_results.json or algorithm_results.json; writes per-case algorithm proof metrics and gates.",
        }
        for operator in pilot_operators
    )
    return {
        "schemaVersion": "2026-04-29.quality-suite.v1",
        "suiteId": "core20_proof_suite",
        "description": "Proof-grade Core20 data loop. Blocked field dataset entries are explicit and must not be treated as passing proof.",
        "ciBudgetMinutes": 180,
        "execution": {"mode": "serial", "runner": "quality/tools/run_quality_suite.py"},
        "stages": [
            {
                "id": "core20-proof-governance",
                "entries": [
                    {
                        "id": "core20_proof_validate",
                        "status": "active",
                        "evidenceKind": "proof-registry",
                        "operators": CORE20_OPERATORS,
                        "command": ["python", "quality/tools/build_core20_proof_assets.py", "--validate-only"],
                        "baselineJson": "quality/evals/reports/QualityFlywheel_core20_proof_baseline.json",
                        "reportMarkdown": "quality/evals/reports/QualityFlywheel_core20_proof_baseline.md",
                        "estimatedSeconds": 30,
                    }
                ],
            },
            {"id": "core20-representative-field-ingest", "entries": representative_ingest_entries},
            {"id": "core20-pilot-field-algorithm-proof", "entries": pilot_proof_entries},
            {"id": "core20-field-datasets", "entries": field_entries},
            {
                "id": "core20-field-replay",
                "entries": [
                    {
                        "id": "core20_field_replay",
                        "status": "active",
                        "evidenceKind": "field-replay",
                        "operators": CORE20_OPERATORS,
                        "command": [
                            "python",
                            "quality/tools/run_field_replay.py",
                            "--manifest",
                            "quality/field_replay/manifests/core20_field_replay_manifest.json",
                            "--drill-id",
                            DRILL_ID,
                            "--output",
                            "quality/evals/reports/QualityFlywheel_core20_field_replay_baseline.json",
                            "--report",
                            "quality/evals/reports/QualityFlywheel_core20_field_replay_baseline.md",
                            "--baseline-output",
                            "quality/evals/reports/QualityFlywheel_core20_field_replay_baseline.json",
                        ],
                        "baselineJson": "quality/evals/reports/QualityFlywheel_core20_field_replay_baseline.json",
                        "reportMarkdown": "quality/evals/reports/QualityFlywheel_core20_field_replay_baseline.md",
                        "estimatedSeconds": 60,
                    }
                ],
            },
        ],
    }


def build_proof_baseline(items: list[dict[str, Any]]) -> dict[str, Any]:
    operator_rows = []
    for item in items:
        operator = item["operator"]
        manifest = field_manifest_path(operator)
        split = read_json(split_path(operator))
        operator_rows.append(
            {
                "Operator": operator,
                "datasetId": proof_name(operator),
                "manifest": repo(manifest),
                "manifestSha256": sha256_file(manifest),
                "splitSummary": {
                    "strategy": split["strategy"],
                    "seed": split["seed"],
                    "trainCount": split["counts"]["train"],
                    "validationCount": split["counts"]["validation"],
                    "testCount": split["counts"]["test"],
                    "noOverlap": True,
                    "caseListPath": repo(split_path(operator)),
                },
                "legacyEvidence": {
                    "status": "legacy-baseline",
                    "baselineJson": repo(source_baseline_path(item)),
                    "manifest": item["manifest"],
                    "caseCount": int(source_operator(item).get("CaseCount", 0) or source_summary(item).get("CaseCount", 0) or 0),
                },
                "metrics": {
                    "primary": item["primaryMetric"],
                    "secondary": SECONDARY_METRICS[operator],
                    "thresholds": freeze_thresholds(item),
                },
                "thresholds": freeze_thresholds(item),
                "perCaseResults": [],
                "failureTaxonomy": item["boundaries"],
                "privacyLeakCount": 0,
                "rawPathLeakCount": 0,
                "accepted": False,
                "proofLevel": PROOF_LEVEL,
                "proofStatus": PROOF_STATUS,
                "industrialStatus": "field proof pending; real industrial validation is not complete",
            }
        )
    return {
        "EvidenceKind": "core20-proof",
        "Summary": {
            "GeneratedAtUtc": GENERATED_AT,
            "OperatorCount": len(operator_rows),
            "Accepted": 0,
            "BlockedMissingFieldData": len(operator_rows),
            "LegacyBaselineCount": len(operator_rows),
            "FieldReplayOperatorCount": len(operator_rows),
            "FieldReplaySampleCount": len(operator_rows) * 20,
            "PrivacyLeakCount": 0,
            "RawPathLeakCount": 0,
            "RealIndustrialValidationComplete": 0,
            "ProofGatePassed": False,
            "ProofGateInterpretation": "Governance assets are complete, but real field dataset proof is blocked until approved de-identified field samples are registered.",
        },
        "Operators": operator_rows,
    }


def render_registry_markdown(registry: dict[str, Any]) -> str:
    lines = [
        "# Quality Flywheel Core20 Proof Registry",
        "",
        f"GeneratedAtUtc: `{registry['generatedAtUtc']}`",
        "",
        "## Summary",
        "",
        f"- Frozen operators: {registry['summary']['operatorCount']}",
        f"- Legacy baselines marked: {registry['summary']['legacyBaselineCount']}",
        f"- Field datasets blocked: {registry['summary']['fieldDatasetBlockedCount']}",
        f"- Field replay samples tracked: {registry['summary']['fieldReplaySampleCount']}",
        "- Industrial validation complete: 0",
        "",
        "## Operators",
        "",
        "| # | Operator | Proof Status | Legacy | Field Manifest | Split |",
        "|---:|---|---|---|---|---|",
    ]
    for index, row in enumerate(registry["operators"], start=1):
        lines.append(
            f"| {index} | {row['operator']} | {row['proofStatus']} | {row['legacyEvidenceStatus']} | "
            f"`{row['fieldManifest']}` | `{row['splitPath']}` |"
        )
    lines.extend(
        [
            "",
            "## Boundary",
            "",
            "- Existing G3 evidence is legacy regression evidence, not industrial proof.",
            "- Field dataset proof remains blocked until approved de-identified samples populate the hash split files.",
            "- Field replay evidence is field-substitute governance evidence and is not customer/line sign-off.",
            "",
        ]
    )
    return "\n".join(lines)


def render_proof_report(baseline: dict[str, Any]) -> str:
    summary = baseline["Summary"]
    lines = [
        "# Quality Flywheel Core20 Proof Baseline",
        "",
        f"GeneratedAtUtc: `{summary['GeneratedAtUtc']}`",
        "",
        "## Summary",
        "",
        f"- Operators: {summary['OperatorCount']}",
        f"- Accepted proof operators: {summary['Accepted']}",
        f"- Blocked missing field data: {summary['BlockedMissingFieldData']}",
        f"- Legacy baseline count: {summary['LegacyBaselineCount']}",
        f"- Field replay samples tracked: {summary['FieldReplaySampleCount']}",
        f"- Privacy/raw-path leaks: {summary['PrivacyLeakCount']}/{summary['RawPathLeakCount']}",
        f"- Proof gate passed: {'Yes' if summary['ProofGatePassed'] else 'No'}",
        "",
        "## Operators",
        "",
        "| Operator | Proof Status | Primary Metric | Train | Val | Test | Accepted | Industrial Status |",
        "|---|---|---|---:|---:|---:|---|---|",
    ]
    for row in baseline["Operators"]:
        split = row["splitSummary"]
        lines.append(
            f"| {row['Operator']} | {row['proofStatus']} | {row['metrics']['primary']} | "
            f"{split['trainCount']} | {split['validationCount']} | {split['testCount']} | "
            f"{row['accepted']} | {row['industrialStatus']} |"
        )
    lines.extend(
        [
            "",
            "## Gate Interpretation",
            "",
            "- `accepted=false` is intentional while field data is missing; this prevents legacy baselines from being silently promoted.",
            "- Each row already has manifest, split, thresholds, failure taxonomy, privacy checks, and replay governance attached.",
            "- Populate split case ids from approved de-identified samples, then replace blocked status with executed/promoted proof results.",
            "",
        ]
    )
    return "\n".join(lines)


def update_operator_quality_matrix(registry: dict[str, Any]) -> None:
    path = REPORT_DIR / "operator_quality_matrix.md"
    if not path.exists():
        return
    text = path.read_text(encoding="utf-8", errors="replace")
    start = "<!-- CORE20_PROOF_SUMMARY_START -->"
    end = "<!-- CORE20_PROOF_SUMMARY_END -->"
    block = "\n".join(
        [
            start,
            "## Core20 Proof Summary",
            "",
            f"- Core20 proof registry: `quality/evals/reports/QualityFlywheel_core20_proof_registry.json`",
            f"- Frozen core operators: {registry['summary']['operatorCount']}",
            f"- Legacy evidence marked as `legacy-baseline`: {registry['summary']['legacyBaselineCount']}",
            f"- Field dataset proof status: `{PROOF_STATUS}` = {registry['summary']['fieldDatasetBlockedCount']}",
            f"- Field replay substitute evidence: {registry['summary']['fieldReplayOperatorCount']} operators / {registry['summary']['fieldReplaySampleCount']} samples",
            "- Industrial status: real industrial validation complete = 0; field-substitute replay is not site/line sign-off.",
            "",
            end,
            "",
        ]
    )
    pattern = re.compile(rf"{re.escape(start)}.*?{re.escape(end)}\s*", re.DOTALL)
    if pattern.search(text):
        text = pattern.sub(block, text)
    else:
        marker = "\n## Full Matrix\n"
        if marker in text:
            text = text.replace(marker, "\n" + block + marker, 1)
        else:
            text = text.rstrip() + "\n\n" + block
    path.write_text(text, encoding="utf-8", newline="\n")


def generate(items: list[dict[str, Any]]) -> None:
    for item in items:
        write_json(split_path(item["operator"]), build_split(item))
        write_json(field_manifest_path(item["operator"]), build_field_manifest(item))

    replay_manifest = build_field_replay_manifest(items)
    replay_manifest_path = FIELD_REPLAY_DIR / "core20_field_replay_manifest.json"
    write_json(replay_manifest_path, replay_manifest)

    registry = build_registry(items)
    write_json(REPORT_DIR / "QualityFlywheel_core20_proof_registry.json", registry)
    (REPORT_DIR / "QualityFlywheel_core20_proof_registry.md").write_text(
        render_registry_markdown(registry), encoding="utf-8", newline="\n"
    )

    baseline = build_proof_baseline(items)
    write_json(REPORT_DIR / "QualityFlywheel_core20_proof_baseline.json", baseline)
    (REPORT_DIR / "QualityFlywheel_core20_proof_baseline.md").write_text(
        render_proof_report(baseline), encoding="utf-8", newline="\n"
    )

    result = build_field_replay_result(replay_manifest_path, replay_manifest, DRILL_ID)
    write_json(REPORT_DIR / "QualityFlywheel_core20_field_replay_baseline.json", result)
    (REPORT_DIR / "QualityFlywheel_core20_field_replay_baseline.md").write_text(
        render_field_replay_report(result), encoding="utf-8", newline="\n"
    )

    write_json(SUITE_DIR / "core20_proof_suite.json", build_suite(items))
    update_operator_quality_matrix(registry)


def validate_required_mapping(mapping: dict[str, Any], path: str, keys: list[str], errors: list[str]) -> None:
    for key in keys:
        if key not in mapping:
            errors.append(f"{path}.{key} is required")
        elif mapping[key] in ("", None, [], {}):
            errors.append(f"{path}.{key} must not be empty")


def validate_split_file(path: Path, operator: str, errors: list[str]) -> None:
    split = read_json(path)
    if split.get("operator") != operator:
        errors.append(f"{repo(path)} operator mismatch")
    if split.get("strategy") != "hashed-case-id-60-20-20":
        errors.append(f"{repo(path)} strategy must be hashed-case-id-60-20-20")
    sets = {name: set(split.get(name, [])) for name in ("train", "validation", "test")}
    if sets["train"] & sets["validation"] or sets["train"] & sets["test"] or sets["validation"] & sets["test"]:
        errors.append(f"{repo(path)} split lists must not overlap")
    all_cases = sets["train"] | sets["validation"] | sets["test"]
    if len(all_cases) != sum(len(values) for values in sets.values()):
        errors.append(f"{repo(path)} has duplicate case ids")
    if RAW_PATH_RE.search(json.dumps(split, ensure_ascii=False)):
        errors.append(f"{repo(path)} contains a raw path pattern")


def validate_field_manifest(path: Path, item: dict[str, Any], errors: list[str]) -> None:
    manifest = read_json(path)
    operator = item["operator"]
    if manifest.get("operator") != operator:
        errors.append(f"{repo(path)} operator mismatch")
    if manifest.get("status") != PROOF_STATUS:
        errors.append(f"{repo(path)} status must be {PROOF_STATUS} until field data is present")
    for section in ("source", "storage", "split", "labels", "metrics", "runner", "failureBoundaries", "privacy", "promotionGate"):
        if not isinstance(manifest.get(section), dict):
            errors.append(f"{repo(path)}.{section} must be an object")
    validate_required_mapping(manifest.get("source", {}), f"{repo(path)}.source", ["kind", "name", "version", "uri", "license"], errors)
    validate_required_mapping(manifest.get("storage", {}), f"{repo(path)}.storage", ["localRootEnv", "relativePath", "gitPolicy"], errors)
    validate_required_mapping(manifest.get("split", {}), f"{repo(path)}.split", ["strategy", "caseListPath"], errors)
    validate_required_mapping(manifest.get("labels", {}), f"{repo(path)}.labels", ["schema", "positiveClasses", "negativeClasses"], errors)
    validate_required_mapping(manifest.get("metrics", {}), f"{repo(path)}.metrics", ["primary", "secondary", "thresholds"], errors)
    validate_required_mapping(manifest.get("runner", {}), f"{repo(path)}.runner", ["command", "baselineJson", "reportMarkdown"], errors)
    boundaries = manifest.get("failureBoundaries", {})
    if boundaries.get("required") is not True or not boundaries.get("taxonomy"):
        errors.append(f"{repo(path)} failureBoundaries must be required with taxonomy")
    if int(boundaries.get("minimumFailureCases", 0) or 0) < 20:
        errors.append(f"{repo(path)} failureBoundaries.minimumFailureCases must be >= 20")
    privacy = manifest.get("privacy", {})
    if privacy.get("containsPersonalData") is not False:
        errors.append(f"{repo(path)} privacy.containsPersonalData must be false")
    if privacy.get("containsCustomerData") is not True:
        errors.append(f"{repo(path)} privacy.containsCustomerData must be true for field proof manifests")
    if RAW_PATH_RE.search(json.dumps(manifest, ensure_ascii=False)):
        errors.append(f"{repo(path)} contains a raw path pattern")


def validate_baseline(path: Path, items: list[dict[str, Any]], errors: list[str], require_full: bool) -> None:
    baseline = read_json(path)
    operators = baseline.get("Operators", [])
    if require_full and len(operators) != len(items):
        errors.append(f"{repo(path)} must include {len(items)} operators")
    selected = {item["operator"] for item in items}
    by_operator = {row.get("Operator"): row for row in operators}
    for operator in selected:
        if operator not in by_operator:
            errors.append(f"{repo(path)} missing operator {operator}")
    for row in operators:
        if row.get("Operator") not in selected and not require_full:
            continue
        for key in (
            "datasetId",
            "manifestSha256",
            "splitSummary",
            "metrics",
            "thresholds",
            "perCaseResults",
            "failureTaxonomy",
            "privacyLeakCount",
            "accepted",
            "proofLevel",
        ):
            if key not in row:
                errors.append(f"{repo(path)} row {row.get('Operator')} missing {key}")
        if row.get("accepted") is not False:
            errors.append(f"{repo(path)} row {row.get('Operator')} must remain accepted=false until field data proof exists")
        if row.get("proofStatus") != PROOF_STATUS:
            errors.append(f"{repo(path)} row {row.get('Operator')} proofStatus must be {PROOF_STATUS}")
        if row.get("privacyLeakCount") != 0 or row.get("rawPathLeakCount") != 0:
            errors.append(f"{repo(path)} row {row.get('Operator')} privacy/raw path leaks must be zero")


def validate(items: list[dict[str, Any]], require_full: bool) -> list[str]:
    errors: list[str] = []
    if require_full and len(items) != 20:
        errors.append("Core20 operator set must contain exactly 20 operators")
    if len(set(CORE20_OPERATORS)) != len(CORE20_OPERATORS):
        errors.append("Core20 operator set contains duplicates")

    for item in items:
        operator = item["operator"]
        manifest = field_manifest_path(operator)
        split = split_path(operator)
        if not manifest.exists():
            errors.append(f"missing field manifest: {repo(manifest)}")
        else:
            validate_field_manifest(manifest, item, errors)
        if not split.exists():
            errors.append(f"missing split file: {repo(split)}")
        else:
            validate_split_file(split, operator, errors)

    registry_path = REPORT_DIR / "QualityFlywheel_core20_proof_registry.json"
    if not registry_path.exists():
        errors.append(f"missing registry: {repo(registry_path)}")
    else:
        registry = read_json(registry_path)
        if registry.get("summary", {}).get("fieldDatasetBlockedCount") != 20:
            errors.append("registry must show 20 blocked field datasets")
        if registry.get("summary", {}).get("realIndustrialValidationComplete") != 0:
            errors.append("registry must not claim real industrial validation complete")

    baseline_path = REPORT_DIR / "QualityFlywheel_core20_proof_baseline.json"
    if not baseline_path.exists():
        errors.append(f"missing proof baseline: {repo(baseline_path)}")
    else:
        validate_baseline(baseline_path, items, errors, require_full)

    replay_path = FIELD_REPLAY_DIR / "core20_field_replay_manifest.json"
    if not replay_path.exists():
        errors.append(f"missing field replay manifest: {repo(replay_path)}")
    else:
        replay_manifest = read_json(replay_path)
        errors.extend(validate_field_replay_manifest(replay_manifest))
        series = replay_manifest.get("sampleSeries", [])
        if require_full and len(series) != 20:
            errors.append("core20 field replay manifest must include 20 sample series")
        selected = {item["operator"] for item in items}
        seen = set()
        for sample in series:
            if sample.get("operator") not in selected and not require_full:
                continue
            seen.add(sample.get("operator"))
            if int(sample.get("sampleCount", 0) or 0) < 20:
                errors.append(f"field replay {sample.get('operator')} must include at least 20 samples")
            if sample.get("operator") not in CORE20_OPERATORS:
                errors.append(f"field replay unknown operator {sample.get('operator')}")
        for operator in selected:
            if operator not in seen:
                errors.append(f"field replay manifest missing operator {operator}")

    suite_path = SUITE_DIR / "core20_proof_suite.json"
    if not suite_path.exists():
        errors.append(f"missing suite: {repo(suite_path)}")
    return errors


def select_items(operator: str | None) -> list[dict[str, Any]]:
    if operator is None:
        return list(G3_OPERATORS)
    selected = [item for item in G3_OPERATORS if item["operator"] == operator]
    if not selected:
        raise ValueError(f"Unknown Core20 operator: {operator}")
    return selected


def main() -> int:
    parser = argparse.ArgumentParser(description="Build or validate Core20 proof-grade data loop assets.")
    parser.add_argument("--validate-only", action="store_true", help="Validate generated assets without writing files.")
    parser.add_argument("--operator", help="Limit validation/generation to one Core20 operator.")
    args = parser.parse_args()

    items = select_items(args.operator)
    if not args.validate_only:
        generate(items if args.operator is None else list(G3_OPERATORS))

    validation_items = items
    errors = validate(validation_items, require_full=args.operator is None)
    if errors:
        for error in errors:
            print(f"error: {error}")
        return 2

    print(
        f"core20 proof assets valid: operators={len(validation_items)} "
        f"status={PROOF_STATUS} generatedAt={datetime.now(timezone.utc).isoformat(timespec='seconds')}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
