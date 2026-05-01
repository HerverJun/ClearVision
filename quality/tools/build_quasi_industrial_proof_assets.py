from __future__ import annotations

import argparse
import hashlib
import json
import re
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from generate_operator_quality_matrix import DEFAULT_CARD_DIR, DEFAULT_CATALOG, parse_catalog


REPO_ROOT = Path(__file__).resolve().parents[2]
DATASET_DIR = REPO_ROOT / "quality" / "datasets"
MANIFEST_DIR = DATASET_DIR / "manifests"
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
SUITE_DIR = REPO_ROOT / "quality" / "evals" / "suites"
PUBLIC_DATA_ROOT = REPO_ROOT / "quality" / "public_datasets"

GENERATED_AT = "2026-04-29T00:00:00Z"
RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/)")
PROOF_LEVELS = ("missing", "contract", "golden", "public-benchmark", "field-substitute", "real-field")
PROOF_ORDER = {name: index for index, name in enumerate(PROOF_LEVELS)}
CORE20_OPERATORS = {
    "TemplateMatching",
    "AnomalyDetection",
    "DeepLearning",
    "SemanticSegmentation",
    "EdgeDetection",
    "ShapeMatching",
    "GradientShapeMatch",
    "PyramidShapeMatch",
    "AkazeFeatureMatch",
    "OrbFeatureMatch",
    "PlanarMatching",
    "LocalDeformableMatching",
    "CaliperTool",
    "ArcCaliper",
    "ContourDetection",
    "BlobAnalysis",
    "LineMeasurement",
    "CircleMeasurement",
    "GeometricFitting",
    "SurfaceDefectDetection",
}
HIGH_VALUE_OPERATORS = {
    "AnomalyDetection",
    "SurfaceDefectDetection",
    "DeepLearning",
    "TemplateMatching",
    "ShapeMatching",
    "GradientShapeMatch",
    "PyramidShapeMatch",
    "AkazeFeatureMatch",
    "OrbFeatureMatch",
    "PlanarMatching",
    "LocalDeformableMatching",
    "CaliperTool",
    "ArcCaliper",
    "ContourDetection",
    "BlobAnalysis",
    "LineMeasurement",
    "CircleMeasurement",
    "GeometricFitting",
}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def repo(path: Path) -> str:
    return path.relative_to(REPO_ROOT).as_posix()


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def write_text(path: Path, value: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value, encoding="utf-8", newline="\n")


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def split_markdown_row(line: str) -> list[str]:
    return [cell.strip().replace("`", "") for cell in line.strip().strip("|").split("|")]


def parse_int(value: Any) -> int:
    try:
        return int(str(value).strip())
    except (TypeError, ValueError):
        return 0


def parse_matrix_evidence() -> dict[str, dict[str, Any]]:
    path = REPORT_DIR / "operator_quality_matrix.md"
    rows: dict[str, dict[str, Any]] = {}
    headers: list[str] = []
    in_full_matrix = False
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith("## Full Matrix"):
            in_full_matrix = True
            headers = []
            continue
        if not in_full_matrix:
            continue
        if line.startswith("## "):
            break
        if not line.startswith("|"):
            continue
        cells = split_markdown_row(line)
        if not headers and cells and cells[0] == "OperatorType":
            headers = cells
            continue
        if not headers or cells[0].startswith("---") or len(cells) != len(headers):
            continue
        row = dict(zip(headers, cells))
        operator_type = row.get("OperatorType", "")
        operator = operator_type.split(".")[-1]
        row["Operator"] = operator
        rows[operator] = row
    return rows


def build_source_rows() -> list[dict[str, Any]]:
    matrix_rows = parse_matrix_evidence()
    rows: list[dict[str, Any]] = []
    for item in parse_catalog(DEFAULT_CATALOG, DEFAULT_CARD_DIR):
        evidence = matrix_rows.get(item.operator, {})
        rows.append(
            {
                "Operator": item.operator,
                "OperatorType": item.operator_type,
                "DisplayName": item.display_name,
                "Category": item.category,
                "QScore": item.qscore,
                "ContractCases": evidence.get("ContractCases", 0),
                "GoldenCases": evidence.get("GoldenCases", 0),
                "DatasetCases": evidence.get("DatasetCases", 0),
                "FieldReplayCases": evidence.get("FieldReplayCases", 0),
            }
        )
    return rows


def dataset_record_count(index_name: str) -> int:
    path = DATASET_DIR / index_name
    if not path.exists():
        return 0
    value = read_json(path)
    return int(value.get("record_count", 0) or len(value.get("records", [])))


def dataset_status(local_root: str, index_name: str | None = None) -> str:
    root = REPO_ROOT / local_root
    root_exists = root.exists()
    index_exists = (DATASET_DIR / index_name).exists() if index_name else False
    if root_exists and (index_name is None or index_exists):
        return "available-local"
    if root_exists and any(path.name != "_downloads" or any(path.iterdir()) for path in root.iterdir()):
        return "downloaded-index-pending"
    return "planned"


def public_dataset_cards() -> list[dict[str, Any]]:
    return [
        {
            "datasetId": "kolektorsdd2",
            "name": "Kolektor Surface-Defect Dataset 2",
            "targetOperators": ["SurfaceDefectDetection", "AnomalyDetection"],
            "proofUse": "public industrial surface defect benchmark",
            "sourceUrl": "https://go.vicos.si/kolektorsdd2",
            "license": "public research dataset terms; review before commercial redistribution",
            "localRoot": "quality/public_datasets/kolektorsdd2",
            "indexPath": "quality/datasets/kolektorsdd2_index.json",
            "recordCount": dataset_record_count("kolektorsdd2_index.json"),
            "status": dataset_status("quality/public_datasets/kolektorsdd2", "kolektorsdd2_index.json"),
        },
        {
            "datasetId": "mvtec_ad_lite",
            "name": "MVTec AD Lite",
            "targetOperators": ["AnomalyDetection", "SurfaceDefectDetection"],
            "proofUse": "public industrial anomaly detection benchmark subset",
            "sourceUrl": "https://www.mvtec.com/research-teaching/datasets/mvtec-ad",
            "license": "CC-BY-NC-SA-4.0",
            "localRoot": "quality/public_datasets/mvtec_ad_lite",
            "indexPath": "quality/datasets/mvtec_ad_lite_index.json",
            "recordCount": dataset_record_count("mvtec_ad_lite_index.json"),
            "status": dataset_status("quality/public_datasets/mvtec_ad_lite", "mvtec_ad_lite_index.json"),
        },
        {
            "datasetId": "bsds500",
            "name": "Berkeley Segmentation Dataset and Benchmark 500",
            "targetOperators": ["EdgeDetection", "SemanticSegmentation", "ContourDetection"],
            "proofUse": "public boundary and segmentation benchmark",
            "sourceUrl": "https://www2.eecs.berkeley.edu/Research/Projects/CS/vision/grouping/resources.html",
            "license": "research dataset terms; review before redistribution",
            "localRoot": "quality/public_datasets/bsds500",
            "indexPath": "quality/datasets/bsds500_index.json",
            "recordCount": dataset_record_count("bsds500_index.json"),
            "status": dataset_status("quality/public_datasets/bsds500", "bsds500_index.json"),
        },
        {
            "datasetId": "opencv_calibration_samples",
            "name": "OpenCV calibration samples",
            "targetOperators": ["CameraCalibration", "Undistort", "FisheyeUndistort", "CoordinateTransform"],
            "proofUse": "public calibration sample images and parameter files",
            "sourceUrl": "https://github.com/opencv/opencv/tree/4.x/samples/data",
            "license": "OpenCV project license terms",
            "localRoot": "quality/public_datasets/opencv_calibration_samples",
            "indexPath": "quality/datasets/opencv_calibration_samples_index.json",
            "recordCount": dataset_record_count("opencv_calibration_samples_index.json"),
            "status": dataset_status("quality/public_datasets/opencv_calibration_samples", "opencv_calibration_samples_index.json"),
        },
        {
            "datasetId": "coco2017",
            "name": "COCO 2017 validation images and annotations",
            "targetOperators": ["DeepLearning"],
            "proofUse": "public object detection benchmark bridge",
            "sourceUrl": "https://cocodataset.org/#download",
            "license": "COCO terms; images carry source licenses, annotations are public benchmark data",
            "localRoot": "quality/public_datasets/coco2017",
            "indexPath": "quality/datasets/coco2017_index.json",
            "recordCount": dataset_record_count("coco2017_index.json"),
            "status": dataset_status("quality/public_datasets/coco2017", "coco2017_index.json"),
        },
        {
            "datasetId": "hpatches",
            "name": "HPatches image matching benchmark",
            "targetOperators": ["AkazeFeatureMatch", "OrbFeatureMatch", "PlanarMatching"],
            "proofUse": "public homography and feature matching benchmark",
            "sourceUrl": "https://github.com/hpatches/hpatches-dataset",
            "license": "HPatches research dataset terms",
            "localRoot": "quality/public_datasets/hpatches",
            "indexPath": "quality/datasets/hpatches_index.json",
            "recordCount": dataset_record_count("hpatches_index.json"),
            "status": dataset_status("quality/public_datasets/hpatches", "hpatches_index.json"),
        },
    ]


def classify_family(row: dict[str, Any]) -> str:
    operator = row["Operator"]
    category = row.get("Category", "")
    text = f"{operator} {category}".lower()
    if any(token in text for token in ("communication", "http", "tcp", "serial", "modbus", "mqtt", "omron", "siemens")):
        return "communication"
    if any(token in text for token in ("foreach", "loop", "judgment", "script", "timer", "save", "loader", "database", "extractor", "convert")):
        return "workflow-data"
    if any(token in operator for token in ("DeepLearning", "Anomaly", "Defect", "Segmentation", "Ocr", "CodeRecognition", "DualModal")):
        return "ai-vision"
    if "Match" in operator or "Matching" in operator:
        return "matching-localization"
    if any(token in operator for token in ("Caliper", "Measurement", "Fitting", "Contour", "Blob", "Geometry", "DistanceTransform")):
        return "measurement-geometry"
    if any(token in operator for token in ("Calibration", "Undistort", "CoordinateTransform")):
        return "calibration"
    if any(token in operator for token in ("FFT", "Filter", "Morphology", "Region", "Image", "Texture", "Threshold", "Transform")):
        return "image-processing"
    return "general-operator"


def current_proof_level(row: dict[str, Any]) -> str:
    if parse_int(row.get("FieldReplayCases")) > 0:
        return "field-substitute"
    if parse_int(row.get("DatasetCases")) > 0:
        return "public-benchmark"
    if parse_int(row.get("GoldenCases")) > 0:
        return "golden"
    if parse_int(row.get("ContractCases")) > 0:
        return "contract"
    return "missing"


def target_proof_level(operator: str, family: str) -> str:
    if operator in CORE20_OPERATORS:
        return "field-substitute"
    if family in {"ai-vision", "matching-localization", "measurement-geometry", "calibration", "image-processing"}:
        return "public-benchmark"
    return "contract"


def evidence_claim(level: str, family: str) -> str:
    if level == "field-substitute":
        return "quasi-industrial public/substitute proof"
    if level == "public-benchmark":
        return "public or semisynthetic proof"
    if level == "golden":
        return "golden/oracle proof"
    if level == "contract":
        return "contract behavior proof"
    return "evidence missing"


def recommended_datasets(operator: str, family: str) -> list[str]:
    if operator in {"SurfaceDefectDetection", "AnomalyDetection"} or "Defect" in operator:
        return ["kolektorsdd2", "mvtec_ad_lite"]
    if operator == "DeepLearning":
        return ["coco2017"]
    if operator in {"SemanticSegmentation", "EdgeDetection", "ContourDetection"}:
        return ["bsds500", "semisynthetic-boundary-oracle"]
    if family == "matching-localization":
        return ["hpatches", "semisynthetic-homography-oracle"]
    if family == "calibration":
        return ["opencv_calibration_samples", "semisynthetic-calibration-oracle"]
    if family == "measurement-geometry":
        return ["semisynthetic-geometry-oracle", "bsds500"]
    if family == "communication":
        return ["mock-protocol-replay", "contract-negative-injection"]
    if family == "workflow-data":
        return ["contract-replay", "schema-negative-injection"]
    return ["semisynthetic-oracle"]


def next_action(operator: str, family: str, current: str, target: str) -> str:
    if PROOF_ORDER[current] >= PROOF_ORDER[target]:
        return "Maintain evidence, add failure replay, and keep claim audit passing"
    if target == "field-substitute":
        return "Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy"
    if target == "public-benchmark":
        return f"Attach {', '.join(recommended_datasets(operator, family))} manifest, split, runner, and threshold gate"
    return "Add contract suite coverage and protocol/error replay cases"


def build_operator_registry(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    operators: list[dict[str, Any]] = []
    for row in rows:
        operator = row["Operator"]
        family = classify_family(row)
        current = current_proof_level(row)
        target = target_proof_level(operator, family)
        status = "target-met" if PROOF_ORDER[current] >= PROOF_ORDER[target] else "gap-open"
        has_existing_evidence = any(
            parse_int(row.get(key)) > 0
            for key in ("ContractCases", "GoldenCases", "DatasetCases", "FieldReplayCases")
        )
        operators.append(
            {
                "operator": operator,
                "operatorType": row.get("OperatorType", ""),
                "displayName": row.get("DisplayName", operator),
                "category": row.get("Category", ""),
                "family": family,
                "core20": operator in CORE20_OPERATORS,
                "qScore": parse_int(row.get("QScore")),
                "currentProofLevel": current,
                "targetMinimumProofLevel": target,
                "targetStatus": status,
                "legacyBaselineDisposition": "legacy-evidence-only" if has_existing_evidence else "no-baseline-evidence",
                "promotionGate": "unified runner schema plus audit gate; legacy evidence is not production sign-off",
                "evidenceClaim": evidence_claim(current, family),
                "claimBoundary": "quasi-industrial only; real production-site validation/sign-off remains pending",
                "industrialStatus": "real field sign-off pending; industrial validation not complete",
                "contractCases": parse_int(row.get("ContractCases")),
                "goldenCases": parse_int(row.get("GoldenCases")),
                "datasetCases": parse_int(row.get("DatasetCases")),
                "fieldReplayCases": parse_int(row.get("FieldReplayCases")),
                "recommendedDatasets": recommended_datasets(operator, family),
                "minimumCasePolicy": "100+ cases and 20+ failure/boundary samples for public/semisynthetic proof",
                "nextAction": next_action(operator, family, current, target),
            }
        )
    return operators


def build_registry() -> dict[str, Any]:
    rows = build_source_rows()
    operators = build_operator_registry(rows)
    target_counts = Counter(row["targetMinimumProofLevel"] for row in operators)
    current_counts = Counter(row["currentProofLevel"] for row in operators)
    status_counts = Counter(row["targetStatus"] for row in operators)
    family_counts = Counter(row["family"] for row in operators)
    return {
        "schemaVersion": "2026-04-29.quasi-industrial-proof.v1",
        "generatedAtUtc": GENERATED_AT,
        "scope": "All 155 operators; Core20 remains highest-priority but long-tail evidence is tracked in parallel.",
        "proofModel": {
            "levels": list(PROOF_LEVELS),
            "highestAllowedWithoutFieldData": "field-substitute",
            "realFieldRequirement": "Only approved own production data plus site/line sign-off may use real-field or real industrial validation complete.",
            "legacyBaselinePolicy": "Existing runnable baselines are downgraded to legacy-evidence-only until they pass the unified runner schema and audit gates.",
            "splitPolicy": {
                "train": "parameter tuning only",
                "validation": "threshold freeze only",
                "test": "final proof only; retune after test requires a new proof version",
            },
            "requiredRunnerFields": [
                "datasetId",
                "manifestSha256",
                "splitSummary",
                "metrics",
                "thresholdResults",
                "perCaseResults",
                "failureTaxonomy",
                "privacyLeakCount",
                "accepted",
            ],
        },
        "summary": {
            "operatorCount": len(operators),
            "core20Count": sum(1 for row in operators if row["core20"]),
            "targetMetCount": status_counts["target-met"],
            "gapOpenCount": status_counts["gap-open"],
            "realIndustrialValidationComplete": 0,
            "currentProofLevelCounts": dict(sorted(current_counts.items())),
            "targetProofLevelCounts": dict(sorted(target_counts.items())),
            "familyCounts": dict(sorted(family_counts.items())),
        },
        "publicDatasets": public_dataset_cards(),
        "operators": operators,
    }


def render_registry_markdown(registry: dict[str, Any]) -> str:
    summary = registry["summary"]
    lines = [
        "# Quality Flywheel 155 Quasi-Industrial Proof Registry",
        "",
        f"GeneratedAtUtc: `{registry['generatedAtUtc']}`",
        "",
        "## Summary",
        "",
        f"- Operators: {summary['operatorCount']}",
        f"- Core20 operators: {summary['core20Count']}",
        f"- Target met: {summary['targetMetCount']}",
        f"- Gap open: {summary['gapOpenCount']}",
        "- Real industrial validation complete: 0",
        "- Claim boundary: quasi-industrial public/substitute evidence only; real field sign-off remains pending.",
        "",
        "## Public Dataset Plan",
        "",
        "| Dataset | Status | Records | Proof Use |",
        "|---|---|---:|---|",
    ]
    for dataset in registry["publicDatasets"]:
        lines.append(
            f"| {dataset['datasetId']} | {dataset['status']} | {dataset['recordCount']} | {dataset['proofUse']} |"
        )
    lines.extend(
        [
            "",
            "## Operator Gaps",
            "",
            "| Operator | Family | Current | Target | Status | Next Action |",
            "|---|---|---|---|---|---|",
        ]
    )
    for row in registry["operators"]:
        if row["targetStatus"] != "gap-open":
            continue
        lines.append(
            f"| {row['operator']} | {row['family']} | {row['currentProofLevel']} | "
            f"{row['targetMinimumProofLevel']} | {row['targetStatus']} | {row['nextAction']} |"
        )
    lines.extend(
        [
            "",
            "## Audit Boundary",
            "",
            "- Public benchmark and semisynthetic evidence may support quasi-industrial claims.",
            "- Real production-site validation is blocked until own field data and sign-off are attached.",
            "",
        ]
    )
    return "\n".join(lines)


def active_validate_entry(entry_id: str, command: list[str], evidence_kind: str, baseline: str, report: str) -> dict[str, Any]:
    return {
        "id": entry_id,
        "status": "active",
        "evidenceKind": evidence_kind,
        "command": command,
        "baselineJson": baseline,
        "reportMarkdown": report,
        "estimatedSeconds": 30,
    }


def build_public_benchmark_suite(registry: dict[str, Any]) -> dict[str, Any]:
    download_entries: list[dict[str, Any]] = []
    for dataset in registry["publicDatasets"]:
        dataset_id = dataset["datasetId"]
        if dataset_id == "mvtec_ad_lite":
            command = [
                "powershell",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                "quality/datasets/download_mvtec_ad_lite.ps1",
            ]
        elif dataset_id in {"coco2017", "hpatches", "bsds500", "kolektorsdd2", "opencv_calibration_samples"}:
            command = [
                "powershell",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                "quality/datasets/download_public_quality_datasets.ps1",
                "-Dataset",
                dataset_id,
            ]
        else:
            continue

        download_entries.append(
            {
                "id": f"download_{dataset_id}",
                "status": "manual",
                "evidenceKind": "public-dataset-download",
                "datasetId": dataset_id,
                "command": command,
                "estimatedSeconds": 1800,
                "notes": "Large public datasets are downloaded only on explicit request; repo stores manifests and indexes, not images.",
            }
        )
    return {
        "schemaVersion": "2026-04-29.quality-suite.v1",
        "suiteId": "public_benchmark_suite",
        "description": "Public benchmark dataset inventory, download hooks, and manifest validation for quasi-industrial proof.",
        "ciBudgetMinutes": 60,
        "execution": {"mode": "serial", "runner": "quality/tools/run_quality_suite.py"},
        "stages": [
            {
                "id": "public-benchmark-inventory",
                "entries": [
                    active_validate_entry(
                        "public_benchmark_inventory_validate",
                        ["python", "quality/tools/build_quasi_industrial_proof_assets.py", "--validate-only", "--focus", "datasets"],
                        "public-benchmark-inventory",
                        "quality/evals/reports/QualityFlywheel_155_quasi_industrial_registry.json",
                        "quality/evals/reports/QualityFlywheel_155_quasi_industrial_registry.md",
                    ),
                    active_validate_entry(
                        "public_benchmark_proof_validate",
                        ["python", "quality/tools/run_public_benchmark_proof.py", "--validate-only"],
                        "public-benchmark-proof",
                        "quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.json",
                        "quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.md",
                    )
                ],
            },
            {"id": "public-benchmark-downloads", "entries": download_entries},
        ],
    }


def build_full155_suite() -> dict[str, Any]:
    return {
        "schemaVersion": "2026-04-29.quality-suite.v1",
        "suiteId": "full155_quality_suite",
        "description": "Full 155-operator quasi-industrial evidence gate; runs validations and keeps gaps explicit.",
        "ciBudgetMinutes": 180,
        "execution": {"mode": "serial", "runner": "quality/tools/run_quality_suite.py"},
        "stages": [
            {
                "id": "registry-and-audit",
                "entries": [
                    active_validate_entry(
                        "quasi_industrial_registry_validate",
                        ["python", "quality/tools/build_quasi_industrial_proof_assets.py", "--validate-only"],
                        "quasi-industrial-registry",
                        "quality/evals/reports/QualityFlywheel_155_quasi_industrial_registry.json",
                        "quality/evals/reports/QualityFlywheel_155_quasi_industrial_registry.md",
                    ),
                    active_validate_entry(
                        "quasi_industrial_audit",
                        ["python", "quality/tools/audit_quasi_industrial_proof.py"],
                        "quasi-industrial-audit",
                        "quality/evals/reports/QualityFlywheel_155_quasi_industrial_audit.json",
                        "quality/evals/reports/QualityFlywheel_155_quasi_industrial_audit.md",
                    ),
                    active_validate_entry(
                        "public_benchmark_proof_validate",
                        ["python", "quality/tools/run_public_benchmark_proof.py", "--validate-only"],
                        "public-benchmark-proof",
                        "quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.json",
                        "quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.md",
                    ),
                ],
            },
            {
                "id": "existing-suite-validation",
                "entries": [
                    active_validate_entry(
                        "quick_contract_suite_validate",
                        ["python", "quality/tools/run_quality_suite.py", "--suite", "quick_contract_suite", "--validate-only"],
                        "contract-suite-validation",
                        "quality/evals/reports/QualityFlywheel_155_quasi_industrial_registry.json",
                        "quality/evals/reports/QualityFlywheel_155_quasi_industrial_registry.md",
                    ),
                    active_validate_entry(
                        "golden_core50_suite_validate",
                        ["python", "quality/tools/run_quality_suite.py", "--suite", "golden_core50_suite", "--validate-only"],
                        "golden-suite-validation",
                        "quality/evals/reports/QualityFlywheel_155_quasi_industrial_registry.json",
                        "quality/evals/reports/QualityFlywheel_155_quasi_industrial_registry.md",
                    ),
                    active_validate_entry(
                        "dataset_heavy_suite_validate",
                        ["python", "quality/tools/run_quality_suite.py", "--suite", "dataset_heavy_suite", "--validate-only"],
                        "dataset-suite-validation",
                        "quality/evals/reports/QualityFlywheel_155_quasi_industrial_registry.json",
                        "quality/evals/reports/QualityFlywheel_155_quasi_industrial_registry.md",
                    ),
                    active_validate_entry(
                        "core20_proof_suite_validate",
                        ["python", "quality/tools/run_quality_suite.py", "--suite", "core20_proof_suite", "--validate-only"],
                        "core20-suite-validation",
                        "quality/evals/reports/QualityFlywheel_core20_proof_baseline.json",
                        "quality/evals/reports/QualityFlywheel_core20_proof_baseline.md",
                    ),
                ],
            },
        ],
    }


def build_algorithm_improvement_suite() -> dict[str, Any]:
    manual_entries = [
        {
            "id": "improve_anomaly_surface_defect",
            "status": "manual",
            "evidenceKind": "algorithm-improvement",
            "operators": ["AnomalyDetection", "SurfaceDefectDetection"],
            "command": ["python", "quality/tools/run_quality_suite.py", "--suite", "dataset_heavy_suite", "--dry-run"],
            "estimatedSeconds": 900,
            "notes": "Use failed public/semisynthetic cases to drive A/B algorithm improvements; do not retune against frozen test results.",
        },
        {
            "id": "improve_deep_learning_detection",
            "status": "manual",
            "evidenceKind": "algorithm-improvement",
            "operators": ["DeepLearning"],
            "command": ["python", "quality/tools/run_quality_suite.py", "--suite", "dataset_heavy_suite", "--dry-run"],
            "estimatedSeconds": 900,
            "notes": "Use DeepLearningCocoRealModelRunner with an external ONNX artifact and manifest; keep annotation-seeded COCO proof separate from real-model AP/precision/recall.",
        },
        {
            "id": "improve_matching_family",
            "status": "manual",
            "evidenceKind": "algorithm-improvement",
            "operators": ["TemplateMatching", "AkazeFeatureMatch", "OrbFeatureMatch", "PlanarMatching"],
            "command": ["python", "quality/tools/run_quality_suite.py", "--suite", "dataset_heavy_suite", "--dry-run"],
            "estimatedSeconds": 900,
            "notes": "Use HPatches/semisynthetic homography failures for evidence-driven matching improvements.",
        },
        {
            "id": "improve_caliper_geometry_family",
            "status": "manual",
            "evidenceKind": "algorithm-improvement",
            "operators": ["CaliperTool", "ArcCaliper", "LineMeasurement", "CircleMeasurement", "GeometricFitting"],
            "command": ["python", "quality/tools/run_quality_suite.py", "--suite", "dataset_heavy_suite", "--dry-run"],
            "estimatedSeconds": 900,
            "notes": "Use geometry oracle failures, noise, blur, polarity, and partial-shape cases for A/B improvement.",
        },
    ]
    return {
        "schemaVersion": "2026-04-29.quality-suite.v1",
        "suiteId": "algorithm_improvement_suite",
        "description": "Failure-taxonomy-driven algorithm improvement work queue; entries are manual until A/B runners exist.",
        "ciBudgetMinutes": 60,
        "execution": {"mode": "serial", "runner": "quality/tools/run_quality_suite.py"},
        "stages": [
            {
                "id": "algorithm-improvement-governance",
                "entries": [
                    active_validate_entry(
                        "algorithm_improvement_registry_validate",
                        ["python", "quality/tools/build_quasi_industrial_proof_assets.py", "--validate-only", "--focus", "algorithms"],
                        "algorithm-improvement-registry",
                        "quality/evals/reports/QualityFlywheel_algorithm_improvement_plan.json",
                        "quality/evals/reports/QualityFlywheel_algorithm_improvement_plan.md",
                    ),
                    active_validate_entry(
                        "algorithm_ab_replay_execute",
                        [
                            "python",
                            "quality/tools/run_algorithm_ab_replay.py",
                            "--execute-matching",
                            "--candidate-version",
                            "center_only_v1",
                            "--validation-scope",
                            "matching",
                        ],
                        "algorithm-ab-replay",
                        "quality/evals/reports/QualityFlywheel_algorithm_ab_replay_report.json",
                        "quality/evals/reports/QualityFlywheel_algorithm_ab_replay_report.md",
                    ),
                    active_validate_entry(
                        "matching_replay_safe_profile_gate",
                        ["python", "quality/tools/build_matching_replay_safe_profile_report.py"],
                        "matching-replay-safe-profile",
                        "quality/evals/reports/QualityFlywheel_matching_replay_safe_profile_candidates_v2.json",
                        "quality/evals/reports/QualityFlywheel_matching_replay_safe_profile_candidates_v2.md",
                    ),
                    active_validate_entry(
                        "matching_leaderboard_build",
                        ["python", "quality/tools/build_hpatches_matching_family_leaderboard.py"],
                        "matching-hpatches-leaderboard",
                        "quality/evals/reports/QualityFlywheel_hpatches_matching_family_leaderboard.json",
                        "quality/evals/reports/QualityFlywheel_hpatches_matching_family_leaderboard.md",
                    ),
                    active_validate_entry(
                        "matching_algorithm_improvement_report_build",
                        ["python", "quality/tools/build_matching_algorithm_improvement_report.py"],
                        "matching-algorithm-improvement-report",
                        "quality/evals/reports/QualityFlywheel_matching_algorithm_improvement_v1.json",
                        "quality/evals/reports/QualityFlywheel_matching_algorithm_improvement_v1.md",
                    ),
                    {
                        "id": "measurement_precision_stress_v2_execute",
                        "status": "active",
                        "evidenceKind": "measurement-precision-stress",
                        "operators": ["CaliperTool", "ArcCaliper", "LineMeasurement", "CircleMeasurement", "GeometricFitting"],
                        "command": [
                            "dotnet",
                            "run",
                            "--project",
                            "quality/tools/MeasurementGeometryOracleRunner/MeasurementGeometryOracleRunner.csproj",
                            "--",
                            "--profile",
                            "stress-v2",
                        ],
                        "baselineJson": "quality/evals/reports/QualityFlywheel_measurement_precision_stress_v2.json",
                        "reportMarkdown": "quality/evals/reports/QualityFlywheel_measurement_precision_stress_v2.md",
                        "estimatedSeconds": 120,
                    },
                    {
                        "id": "shape_matching_precision_v2_execute",
                        "status": "active",
                        "evidenceKind": "shape-matching-precision",
                        "operators": ["TemplateMatching", "ShapeMatching", "GradientShapeMatch", "PyramidShapeMatch"],
                        "command": [
                            "python",
                            "quality/tools/build_shape_matching_precision_v2.py",
                            "--execute-candidates",
                        ],
                        "baselineJson": "quality/evals/reports/QualityFlywheel_shape_matching_precision_v2.json",
                        "reportMarkdown": "quality/evals/reports/QualityFlywheel_shape_matching_precision_v2.md",
                        "estimatedSeconds": 180,
                    },
                    {
                        "id": "anomaly_detection_threshold_calibration_v1",
                        "status": "active",
                        "evidenceKind": "anomaly-threshold-calibration",
                        "operators": ["AnomalyDetection"],
                        "command": ["python", "quality/tools/build_anomaly_threshold_calibration_v1.py"],
                        "baselineJson": "quality/evals/reports/QualityFlywheel_anomaly_threshold_calibration_v1.json",
                        "reportMarkdown": "quality/evals/reports/QualityFlywheel_anomaly_threshold_calibration_v1.md",
                        "estimatedSeconds": 15,
                    },
                    {
                        "id": "anomaly_detection_candidate_v2_execute",
                        "status": "active",
                        "evidenceKind": "anomaly-candidate-v2",
                        "operators": ["AnomalyDetection"],
                        "command": [
                            "python",
                            "quality/tools/build_anomaly_threshold_calibration_v1.py",
                            "--execute-candidate",
                        ],
                        "baselineJson": "quality/evals/reports/AnomalyDetection_mvtec_candidate_v2.json",
                        "reportMarkdown": "quality/evals/reports/AnomalyDetection_mvtec_candidate_v2.md",
                        "estimatedSeconds": 75,
                    },
                    {
                        "id": "detection_ab_replay_execute",
                        "status": "active",
                        "evidenceKind": "detection-ab-replay",
                        "operators": ["SurfaceDefectDetection", "AnomalyDetection", "EdgeDetection"],
                        "command": [
                            "python",
                            "quality/tools/run_algorithm_ab_replay.py",
                            "--execute-surface-defect",
                            "--execute-anomaly-detection",
                            "--execute-edge-detection",
                            "--anomaly-detection-candidate-version",
                            "v2",
                            "--validation-scope",
                            "detection",
                        ],
                        "baselineJson": "quality/evals/reports/QualityFlywheel_algorithm_ab_replay_report.json",
                        "reportMarkdown": "quality/evals/reports/QualityFlywheel_algorithm_ab_replay_report.md",
                        "estimatedSeconds": 180,
                    },
                    {
                        "id": "edge_detection_recall_guard_sweep",
                        "status": "active",
                        "evidenceKind": "edge-detection-recall-guard-sweep",
                        "operators": ["EdgeDetection"],
                        "command": ["python", "quality/tools/run_edge_detection_bsds_recall_guard_sweep.py"],
                        "baselineJson": "quality/evals/reports/QualityFlywheel_edge_detection_recall_guard_sweep_v1.json",
                        "reportMarkdown": "quality/evals/reports/QualityFlywheel_edge_detection_recall_guard_sweep_v1.md",
                        "estimatedSeconds": 90,
                    },
                    {
                        "id": "detection_precision_v2_build",
                        "status": "active",
                        "evidenceKind": "detection-precision-v2",
                        "operators": ["SurfaceDefectDetection", "AnomalyDetection", "EdgeDetection"],
                        "command": ["python", "quality/tools/build_detection_precision_v2.py"],
                        "baselineJson": "quality/evals/reports/QualityFlywheel_detection_precision_v2.json",
                        "reportMarkdown": "quality/evals/reports/QualityFlywheel_detection_precision_v2.md",
                        "estimatedSeconds": 30,
                    },
                ],
            },
            {"id": "algorithm-improvement-work-queue", "entries": manual_entries},
        ],
    }


def build_audit_suite() -> dict[str, Any]:
    return {
        "schemaVersion": "2026-04-29.quality-suite.v1",
        "suiteId": "audit_suite",
        "description": "Quasi-industrial audit gate for license, privacy, split, reproducibility, and claim boundaries.",
        "ciBudgetMinutes": 15,
        "execution": {"mode": "serial", "runner": "quality/tools/run_quality_suite.py"},
        "stages": [
            {
                "id": "quasi-industrial-audit",
                "entries": [
                    active_validate_entry(
                        "quasi_industrial_claim_audit",
                        ["python", "quality/tools/audit_quasi_industrial_proof.py"],
                        "quasi-industrial-audit",
                        "quality/evals/reports/QualityFlywheel_155_quasi_industrial_audit.json",
                        "quality/evals/reports/QualityFlywheel_155_quasi_industrial_audit.md",
                    )
                ],
            }
        ],
    }


def build_algorithm_plan(registry: dict[str, Any]) -> dict[str, Any]:
    focus = [
        row
        for row in registry["operators"]
        if row["operator"] in HIGH_VALUE_OPERATORS or row["targetStatus"] == "gap-open"
    ]
    return {
        "schemaVersion": "2026-04-29.algorithm-improvement-plan.v1",
        "generatedAtUtc": GENERATED_AT,
        "policy": {
            "strategy": "evidence-driven optimization",
            "abRule": "Every algorithm change must compare old/new metrics, failed case replay, performance, memory, and regression risk.",
            "testRule": "Validation split freezes thresholds; test split is final proof only and requires a new proof version after retuning.",
        },
        "summary": {
            "focusOperatorCount": len(focus),
            "highValueOperatorCount": len(HIGH_VALUE_OPERATORS),
        },
        "operators": [
            {
                "operator": row["operator"],
                "family": row["family"],
                "currentProofLevel": row["currentProofLevel"],
                "targetMinimumProofLevel": row["targetMinimumProofLevel"],
                "recommendedDatasets": row["recommendedDatasets"],
                "nextAction": row["nextAction"],
                "requiredABOutputs": [
                    "oldBaseline",
                    "newBaseline",
                    "failedCaseReplay",
                    "thresholdDelta",
                    "runtimeDelta",
                    "memoryDelta",
                    "regressionRisk",
                ],
            }
            for row in focus
        ],
    }


def render_algorithm_plan(plan: dict[str, Any]) -> str:
    lines = [
        "# Quality Flywheel Algorithm Improvement Plan",
        "",
        f"GeneratedAtUtc: `{plan['generatedAtUtc']}`",
        "",
        "## Policy",
        "",
        f"- Strategy: {plan['policy']['strategy']}",
        f"- A/B rule: {plan['policy']['abRule']}",
        f"- Test rule: {plan['policy']['testRule']}",
        "",
        "## Work Queue",
        "",
        "| Operator | Family | Current | Target | Next Action |",
        "|---|---|---|---|---|",
    ]
    for row in plan["operators"]:
        lines.append(
            f"| {row['operator']} | {row['family']} | {row['currentProofLevel']} | "
            f"{row['targetMinimumProofLevel']} | {row['nextAction']} |"
        )
    lines.append("")
    return "\n".join(lines)


def validate_registry(registry: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    if registry.get("summary", {}).get("operatorCount") != 155:
        errors.append("registry must include exactly 155 operators")
    if registry.get("summary", {}).get("realIndustrialValidationComplete") != 0:
        errors.append("registry must not claim real industrial validation complete")
    operators = registry.get("operators", [])
    seen = set()
    for row in operators:
        operator = row.get("operator")
        if operator in seen:
            errors.append(f"duplicate operator row: {operator}")
        seen.add(operator)
        if row.get("currentProofLevel") not in PROOF_LEVELS:
            errors.append(f"{operator} has invalid currentProofLevel")
        if row.get("targetMinimumProofLevel") not in PROOF_LEVELS:
            errors.append(f"{operator} has invalid targetMinimumProofLevel")
        if row.get("currentProofLevel") == "real-field":
            errors.append(f"{operator} overclaims real-field proof")
        if str(row.get("industrialStatus", "")).strip().lower() == "real industrial validation complete":
            errors.append(f"{operator} overclaims real industrial validation")
        if row.get("legacyBaselineDisposition") not in {"legacy-evidence-only", "no-baseline-evidence"}:
            errors.append(f"{operator} missing legacy baseline disposition")
        if not row.get("recommendedDatasets"):
            errors.append(f"{operator} missing recommendedDatasets")
    for dataset in registry.get("publicDatasets", []):
        for key in ("datasetId", "name", "sourceUrl", "license", "status"):
            if not dataset.get(key):
                errors.append(f"public dataset {dataset.get('datasetId')} missing {key}")
    if RAW_PATH_RE.search(json.dumps(registry, ensure_ascii=False)):
        errors.append("registry contains raw path pattern")
    return errors


def generate() -> dict[str, Any]:
    registry = build_registry()
    write_json(REPORT_DIR / "QualityFlywheel_155_quasi_industrial_registry.json", registry)
    write_text(REPORT_DIR / "QualityFlywheel_155_quasi_industrial_registry.md", render_registry_markdown(registry))
    write_json(DATASET_DIR / "public_benchmark_dataset_cards.json", {"datasets": registry["publicDatasets"]})
    write_text(DATASET_DIR / "public_benchmark_dataset_cards.md", render_dataset_cards(registry["publicDatasets"]))

    algorithm_plan = build_algorithm_plan(registry)
    write_json(REPORT_DIR / "QualityFlywheel_algorithm_improvement_plan.json", algorithm_plan)
    write_text(REPORT_DIR / "QualityFlywheel_algorithm_improvement_plan.md", render_algorithm_plan(algorithm_plan))

    write_json(SUITE_DIR / "public_benchmark_suite.json", build_public_benchmark_suite(registry))
    write_json(SUITE_DIR / "full155_quality_suite.json", build_full155_suite())
    write_json(SUITE_DIR / "algorithm_improvement_suite.json", build_algorithm_improvement_suite())
    write_json(SUITE_DIR / "audit_suite.json", build_audit_suite())
    return registry


def render_dataset_cards(datasets: list[dict[str, Any]]) -> str:
    lines = [
        "# Public Benchmark Dataset Cards",
        "",
        f"GeneratedAtUtc: `{GENERATED_AT}`",
        "",
        "| Dataset | Status | Records | License | Source |",
        "|---|---|---:|---|---|",
    ]
    for dataset in datasets:
        lines.append(
            f"| {dataset['datasetId']} | {dataset['status']} | {dataset['recordCount']} | "
            f"{dataset['license']} | {dataset['sourceUrl']} |"
        )
    lines.extend(
        [
            "",
            "Large images and archives stay under ignored `quality/public_datasets`; repo artifacts are manifests, indexes, and reports only.",
            "",
        ]
    )
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Build 155-operator quasi-industrial proof assets.")
    parser.add_argument("--validate-only", action="store_true", help="Validate generated assets without writing files.")
    parser.add_argument("--focus", choices=("all", "datasets", "algorithms"), default="all")
    args = parser.parse_args()

    registry = read_json(REPORT_DIR / "QualityFlywheel_155_quasi_industrial_registry.json") if args.validate_only else generate()
    errors = validate_registry(registry)
    if args.focus == "datasets":
        if not registry.get("publicDatasets"):
            errors.append("public dataset cards must not be empty")
    if args.focus == "algorithms":
        plan_path = REPORT_DIR / "QualityFlywheel_algorithm_improvement_plan.json"
        if not plan_path.exists():
            errors.append("algorithm improvement plan is missing")
        elif not read_json(plan_path).get("operators"):
            errors.append("algorithm improvement plan must include operators")
    for suite_name in ("public_benchmark_suite", "full155_quality_suite", "algorithm_improvement_suite", "audit_suite"):
        if not (SUITE_DIR / f"{suite_name}.json").exists():
            errors.append(f"missing suite: {suite_name}")
    if errors:
        for error in errors:
            print(f"error: {error}")
        return 2
    print(
        f"quasi-industrial proof assets valid: operators={registry['summary']['operatorCount']} "
        f"gaps={registry['summary']['gapOpenCount']} focus={args.focus} generatedAt={utc_now()}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
