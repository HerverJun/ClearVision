from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from build_core20_proof_assets import (
    CORE20_OPERATORS,
    REPORT_DIR,
    field_baseline_path,
    field_manifest_path,
    field_report_path,
    freeze_thresholds,
    proof_name,
    read_json,
    render_proof_report,
    render_registry_markdown,
    repo,
    sha256_file,
    split_path,
    write_json,
)
from build_quality_flywheel_g3_closure import G3_OPERATORS


REPO_ROOT = Path(__file__).resolve().parents[2]
INGEST_ENABLED_OPERATORS = (
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
)
REPRESENTATIVE_OPERATORS = INGEST_ENABLED_OPERATORS
CASE_LIST_CANDIDATES = ("cases.json", "cases.jsonl", "case_manifest.json")
SPLIT_SEED = 20260429
MIN_CASES = 100
MIN_FAILURE_BOUNDARY_CASES = 20
RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/)")
SAFE_CASE_ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{7,127}$")
SENSITIVE_KEYS = {
    "absolutepath",
    "customer",
    "customerid",
    "customername",
    "filepath",
    "lotnumber",
    "order",
    "orderid",
    "rawpath",
    "serial",
    "serialnumber",
    "site",
    "siteid",
    "sitename",
}


class IngestError(Exception):
    pass


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def operator_item(operator: str) -> dict[str, Any]:
    for item in G3_OPERATORS:
        if item["operator"] == operator:
            return item
    raise IngestError(f"Unsupported Core20 operator: {operator}")


def resolve_dataset_root(value: str | None) -> Path | None:
    root = value or os.environ.get("CLEARVISION_PRODUCTION_DATASET_ROOT")
    if not root:
        return None
    return Path(root).expanduser().resolve()


def field_root(dataset_root: Path, operator: str) -> Path:
    return dataset_root / "core20" / operator / "field_v1"


def find_case_list(root: Path) -> Path | None:
    for name in CASE_LIST_CANDIDATES:
        candidate = root / name
        if candidate.exists():
            return candidate
    return None


def read_case_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8-sig") as handle:
        return json.load(handle)


def load_cases(case_list_path: Path, operator: str) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    if case_list_path.suffix.lower() == ".jsonl":
        cases = []
        for line_number, line in enumerate(case_list_path.read_text(encoding="utf-8").splitlines(), start=1):
            if not line.strip():
                continue
            value = json.loads(line)
            if not isinstance(value, dict):
                raise IngestError(f"{case_list_path.name}:{line_number} must be a JSON object")
            cases.append(value)
        return cases, {"schemaVersion": "2026-04-29.core20-field-cases.jsonl", "operator": operator}

    value = read_case_json(case_list_path)
    if not isinstance(value, dict):
        raise IngestError(f"{case_list_path.name} root must be an object")
    if value.get("operator") and value.get("operator") != operator:
        raise IngestError(f"{case_list_path.name} operator mismatch: {value.get('operator')} != {operator}")
    cases = value.get("cases")
    if not isinstance(cases, list):
        raise IngestError(f"{case_list_path.name}.cases must be a list")
    return cases, value


def ensure_no_sensitive_json(value: Any, label: str, errors: list[str]) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            key_text = str(key)
            key_norm = re.sub(r"[^a-z0-9]", "", key_text.lower())
            if key_norm in SENSITIVE_KEYS:
                errors.append(f"{label}: sensitive key is forbidden: {key_text}")
            ensure_no_sensitive_json(child, f"{label}.{key_text}", errors)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            ensure_no_sensitive_json(child, f"{label}[{index}]", errors)
    elif isinstance(value, str) and RAW_PATH_RE.search(value):
        errors.append(f"{label}: raw path pattern is forbidden")


def validate_relative_file(root: Path, path_value: Any, label: str, errors: list[str]) -> bool:
    if not isinstance(path_value, str) or not path_value.strip():
        errors.append(f"{label} must be a non-empty relative path")
        return False
    if RAW_PATH_RE.search(path_value):
        errors.append(f"{label} must not contain an absolute/raw path")
        return False
    path = Path(path_value)
    if path.is_absolute() or ".." in path.parts:
        errors.append(f"{label} must stay under the field_v1 dataset root")
        return False
    resolved = (root / path).resolve()
    try:
        resolved.relative_to(root.resolve())
    except ValueError:
        errors.append(f"{label} escapes the field_v1 dataset root")
        return False
    if not resolved.exists():
        errors.append(f"{label} does not exist under field_v1: {path_value}")
        return False
    return True


def case_label(case: dict[str, Any]) -> str:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    value = case.get("label") or labels.get("imageLabel") or labels.get("result")
    return str(value or "").strip().lower()


def taxonomy_values(case: dict[str, Any]) -> list[str]:
    values = case.get("failureTaxonomy") or case.get("failureBoundaries") or case.get("taxonomy") or []
    if isinstance(values, str):
        values = [values]
    if not isinstance(values, list):
        return []
    return [str(value).strip() for value in values if str(value).strip()]


def is_failure_or_boundary(case: dict[str, Any]) -> bool:
    label = case_label(case)
    if label in {
        "ng",
        "fail",
        "defect",
        "anomaly",
        "boundary",
        "boundary_negative",
        "no_edge",
        "no_match",
        "low_feature",
        "low_match",
        "no_contour",
        "no_blob",
        "no_line",
        "no_circle",
        "no_fit",
        "invalid_template",
        "invalid",
        "reject",
    }:
        return True
    return any(value not in {"ok", "good", "negative", "clean-negative"} for value in taxonomy_values(case))


def validate_common_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    label = f"case[{index}]"
    case_id = case.get("caseId")
    if not isinstance(case_id, str) or not SAFE_CASE_ID_RE.match(case_id):
        errors.append(f"{label}.caseId must be a de-identified id using 8-128 chars [A-Za-z0-9_.-]")
    if case.get("redactionStatus") != "approved":
        errors.append(f"{label}.redactionStatus must be approved")
    labels = case.get("labels")
    if not isinstance(labels, dict) or not labels:
        errors.append(f"{label}.labels must be a non-empty object")
    if not taxonomy_values(case):
        errors.append(f"{label}.failureTaxonomy must identify the boundary/failure family")
    files = case.get("files")
    if not isinstance(files, dict) or not files.get("image"):
        errors.append(f"{label}.files.image is required")
    elif validate_relative_file(root, files.get("image"), f"{label}.files.image", errors):
        for file_key, file_value in files.items():
            if file_key == "image":
                continue
            validate_relative_file(root, file_value, f"{label}.files.{file_key}", errors)
    ensure_no_sensitive_json(case, label, errors)


def numeric(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def validate_bbox(value: Any, label: str, errors: list[str]) -> None:
    if not isinstance(value, list) or len(value) != 4 or not all(numeric(item) for item in value):
        errors.append(f"{label}.bbox must be [x, y, width, height] numeric values")
        return
    if float(value[2]) <= 0 or float(value[3]) <= 0:
        errors.append(f"{label}.bbox width/height must be positive")


def validate_positive_number(value: Any, label: str, errors: list[str]) -> None:
    if not numeric(value) or float(value) <= 0:
        errors.append(f"{label} must be positive")


def validate_non_negative_integer(value: Any, label: str, errors: list[str]) -> None:
    if not isinstance(value, int) or isinstance(value, bool) or value < 0:
        errors.append(f"{label} must be a non-negative integer")


def validate_polarity(value: Any, label: str, errors: list[str]) -> None:
    polarity = str(value or "either").lower()
    if polarity not in {"positive", "negative", "either"}:
        errors.append(f"{label} must be positive/negative/either")


def validate_pose(value: Any, label: str, errors: list[str]) -> None:
    if not isinstance(value, dict):
        errors.append(f"{label}.pose must be an object")
        return
    for key in ("x", "y"):
        if not numeric(value.get(key)):
            errors.append(f"{label}.pose.{key} must be numeric")
    for key in ("angleDeg", "scale"):
        if key in value and not numeric(value.get(key)):
            errors.append(f"{label}.pose.{key} must be numeric")
    if "scale" in value and numeric(value.get("scale")) and float(value["scale"]) <= 0:
        errors.append(f"{label}.pose.scale must be positive")


def validate_homography(value: Any, label: str, errors: list[str]) -> None:
    if not isinstance(value, list) or len(value) != 9 or not all(numeric(item) for item in value):
        errors.append(f"{label}.homography must contain 9 numeric values")


def validate_point(value: Any, label: str, errors: list[str]) -> None:
    if isinstance(value, list) and len(value) == 2 and all(numeric(item) for item in value):
        return
    if isinstance(value, dict) and numeric(value.get("x")) and numeric(value.get("y")):
        return
    errors.append(f"{label} must be a numeric [x, y] point or object with x/y")


def validate_point_pairs(value: Any, label: str, errors: list[str]) -> None:
    if not isinstance(value, list) or not value:
        errors.append(f"{label} must be a non-empty list")
        return
    for pair_index, pair in enumerate(value):
        pair_label = f"{label}[{pair_index}]"
        if isinstance(pair, list) and len(pair) == 4 and all(numeric(item) for item in pair):
            continue
        if not isinstance(pair, dict):
            errors.append(f"{pair_label} must be an object or [x1, y1, x2, y2] numeric list")
            continue
        source = pair.get("templatePoint") or pair.get("sourcePoint") or pair.get("source")
        target = pair.get("searchPoint") or pair.get("targetPoint") or pair.get("target")
        validate_point(source, f"{pair_label}.source", errors)
        validate_point(target, f"{pair_label}.target", errors)


def validate_polygon(value: Any, label: str, errors: list[str]) -> None:
    if not isinstance(value, list) or len(value) < 3:
        errors.append(f"{label} must include at least three points")
        return
    for point_index, point in enumerate(value):
        validate_point(point, f"{label}[{point_index}]", errors)


def validate_line_geometry(value: Any, label: str, errors: list[str]) -> None:
    if isinstance(value, list) and len(value) == 4 and all(numeric(item) for item in value):
        return
    if not isinstance(value, dict):
        errors.append(f"{label} must be an object or [x1, y1, x2, y2] numeric list")
        return
    start = value.get("start") or value.get("point1") or value.get("p1")
    end = value.get("end") or value.get("point2") or value.get("p2")
    validate_point(start, f"{label}.start", errors)
    validate_point(end, f"{label}.end", errors)


def validate_circle_geometry(value: Any, label: str, errors: list[str]) -> None:
    if not isinstance(value, dict):
        errors.append(f"{label} must be an object")
        return
    validate_point(value.get("center"), f"{label}.center", errors)
    radius = value.get("radiusPx", value.get("radius"))
    validate_positive_number(radius, f"{label}.radiusPx", errors)


def validate_regions(value: Any, label: str, errors: list[str]) -> None:
    if not isinstance(value, list) or not value:
        errors.append(f"{label} must be a non-empty list")
        return
    for region_index, region in enumerate(value):
        region_label = f"{label}[{region_index}]"
        if isinstance(region, dict):
            if "bbox" in region:
                validate_bbox(region.get("bbox"), region_label, errors)
            elif "polygon" not in region and "mask" not in region:
                errors.append(f"{region_label} must include bbox, polygon, or mask")
        elif isinstance(region, list):
            validate_bbox(region, region_label, errors)
        else:
            errors.append(f"{region_label} must be an object or bbox list")


def validate_label_path(root: Path, value: Any, label: str, errors: list[str]) -> None:
    if isinstance(value, str):
        validate_relative_file(root, value, label, errors)
    elif isinstance(value, list):
        if not value:
            errors.append(f"{label} must not be empty")
        for index, item in enumerate(value):
            if isinstance(item, str):
                validate_relative_file(root, item, f"{label}[{index}]", errors)


def expected_result(labels: dict[str, Any]) -> str:
    return str(labels.get("expectedResult") or labels.get("result") or "").strip().lower()


def is_negative_expected(labels: dict[str, Any]) -> bool:
    return expected_result(labels) in {
        "no_match",
        "not_found",
        "negative",
        "reject",
        "no_edge",
        "invalid",
        "invalid_template",
        "low_feature",
        "low_match",
        "no_contour",
        "no_blob",
        "no_line",
        "no_circle",
        "no_fit",
    }


def validate_surface_defect_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    image_label = str(labels.get("imageLabel") or case.get("label") or "").lower()
    if image_label not in {"ok", "good", "ng", "defect"}:
        errors.append(f"case[{index}].labels.imageLabel must be ok/good/ng/defect")
    if image_label in {"ng", "defect"}:
        if not labels.get("defectType"):
            errors.append(f"case[{index}].labels.defectType is required for NG defect cases")
        regions = labels.get("defectRegions") or labels.get("masks")
        if not isinstance(regions, list) or not regions:
            errors.append(f"case[{index}].labels.defectRegions or masks is required for NG defect cases")


def validate_deep_learning_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    objects = labels.get("objects")
    if objects is None:
        objects = []
    if not isinstance(objects, list):
        errors.append(f"case[{index}].labels.objects must be a list")
        return
    for object_index, item in enumerate(objects):
        if not isinstance(item, dict):
            errors.append(f"case[{index}].labels.objects[{object_index}] must be an object")
            continue
        if not (item.get("className") or item.get("classId") is not None):
            errors.append(f"case[{index}].labels.objects[{object_index}] must include className or classId")
        validate_bbox(item.get("bbox"), f"case[{index}].labels.objects[{object_index}]", errors)
    ignore_regions = labels.get("ignoreRegions", [])
    if ignore_regions and not isinstance(ignore_regions, list):
        errors.append(f"case[{index}].labels.ignoreRegions must be a list")
    for region_index, region in enumerate(ignore_regions if isinstance(ignore_regions, list) else []):
        if isinstance(region, dict):
            validate_bbox(region.get("bbox"), f"case[{index}].labels.ignoreRegions[{region_index}]", errors)
        else:
            validate_bbox(region, f"case[{index}].labels.ignoreRegions[{region_index}]", errors)


def validate_caliper_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    measurements = labels.get("measurements") or labels.get("edgePairs") or []
    if not measurements:
        expected = str(labels.get("expectedResult") or "").lower()
        if expected not in {"no_edge", "invalid", "reject"}:
            errors.append(f"case[{index}].labels.measurements is required unless expectedResult is no_edge/invalid/reject")
        return
    if not isinstance(measurements, list):
        errors.append(f"case[{index}].labels.measurements must be a list")
        return
    for measurement_index, item in enumerate(measurements):
        if not isinstance(item, dict):
            errors.append(f"case[{index}].labels.measurements[{measurement_index}] must be an object")
            continue
        width = item.get("expectedWidthPx", item.get("widthPx"))
        tolerance = item.get("tolerancePx")
        if not numeric(width) or float(width) <= 0:
            errors.append(f"case[{index}].labels.measurements[{measurement_index}].expectedWidthPx must be positive")
        if not numeric(tolerance) or float(tolerance) <= 0:
            errors.append(f"case[{index}].labels.measurements[{measurement_index}].tolerancePx must be positive")
        polarity = str(item.get("polarity") or "either").lower()
        if polarity not in {"positive", "negative", "either"}:
            errors.append(f"case[{index}].labels.measurements[{measurement_index}].polarity must be positive/negative/either")


def validate_arc_caliper_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    if is_negative_expected(labels):
        return
    arcs = labels.get("arcs") or labels.get("arcMeasurements") or labels.get("measurements")
    if not isinstance(arcs, list) or not arcs:
        errors.append(f"case[{index}].labels.arcs or arcMeasurements must be a non-empty list")
        return
    for arc_index, item in enumerate(arcs):
        item_label = f"case[{index}].labels.arcs[{arc_index}]"
        if not isinstance(item, dict):
            errors.append(f"{item_label} must be an object")
            continue
        validate_point(item.get("center"), f"{item_label}.center", errors)
        validate_positive_number(item.get("radiusPx", item.get("radius")), f"{item_label}.radiusPx", errors)
        has_span = numeric(item.get("angularSpanDeg"))
        has_start_end = numeric(item.get("startAngleDeg")) and numeric(item.get("endAngleDeg"))
        if not has_span and not has_start_end:
            errors.append(f"{item_label} must include angularSpanDeg or startAngleDeg/endAngleDeg")
        if has_span and float(item["angularSpanDeg"]) <= 0:
            errors.append(f"{item_label}.angularSpanDeg must be positive")
        validate_positive_number(item.get("tolerancePx"), f"{item_label}.tolerancePx", errors)
        validate_polarity(item.get("polarity"), f"{item_label}.polarity", errors)


def validate_contour_detection_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    contours = labels.get("contours") or labels.get("contourPolygons") or labels.get("polygons")
    contour_count = labels.get("contourCount")
    if contours is None and contour_count is None:
        errors.append(f"case[{index}].labels must include contours/contourPolygons or contourCount")
        return
    if contour_count is not None:
        validate_non_negative_integer(contour_count, f"case[{index}].labels.contourCount", errors)
    if contours is None:
        return
    if not isinstance(contours, list) or not contours:
        errors.append(f"case[{index}].labels.contours must be a non-empty list")
        return
    for contour_index, contour in enumerate(contours):
        contour_label = f"case[{index}].labels.contours[{contour_index}]"
        if isinstance(contour, dict):
            polygon = contour.get("polygon") or contour.get("points")
            if polygon is not None:
                validate_polygon(polygon, f"{contour_label}.polygon", errors)
            elif "bbox" in contour:
                validate_bbox(contour.get("bbox"), contour_label, errors)
            else:
                errors.append(f"{contour_label} must include polygon/points or bbox")
        else:
            validate_polygon(contour, contour_label, errors)
    if "countTolerance" in labels:
        validate_non_negative_integer(labels.get("countTolerance"), f"case[{index}].labels.countTolerance", errors)


def validate_blob_analysis_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    blobs = labels.get("blobs") or labels.get("components")
    blob_count = labels.get("blobCount")
    if blobs is None and blob_count is None:
        errors.append(f"case[{index}].labels must include blobs/components or blobCount")
        return
    if blob_count is not None:
        validate_non_negative_integer(blob_count, f"case[{index}].labels.blobCount", errors)
    if blobs is None:
        return
    if not isinstance(blobs, list):
        errors.append(f"case[{index}].labels.blobs must be a list")
        return
    for blob_index, blob in enumerate(blobs):
        blob_label = f"case[{index}].labels.blobs[{blob_index}]"
        if not isinstance(blob, dict):
            errors.append(f"{blob_label} must be an object")
            continue
        validate_positive_number(blob.get("areaPx", blob.get("area")), f"{blob_label}.areaPx", errors)
        if "centroid" in blob:
            validate_point(blob.get("centroid"), f"{blob_label}.centroid", errors)
        if "bbox" in blob:
            validate_bbox(blob.get("bbox"), blob_label, errors)
        validate_label_path(root, blob.get("mask"), f"{blob_label}.mask", errors)


def validate_line_measurement_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    if is_negative_expected(labels):
        return
    lines = labels.get("lines") or labels.get("measurements")
    if not isinstance(lines, list) or not lines:
        errors.append(f"case[{index}].labels.lines or measurements must be a non-empty list")
        return
    for line_index, item in enumerate(lines):
        item_label = f"case[{index}].labels.lines[{line_index}]"
        geometry = item.get("line") if isinstance(item, dict) and "line" in item else item
        validate_line_geometry(geometry, item_label, errors)
        if isinstance(item, dict):
            validate_positive_number(item.get("tolerancePx"), f"{item_label}.tolerancePx", errors)
            if "lengthPx" in item:
                validate_positive_number(item.get("lengthPx"), f"{item_label}.lengthPx", errors)
            if "angleDeg" in item and not numeric(item.get("angleDeg")):
                errors.append(f"{item_label}.angleDeg must be numeric")


def validate_circle_measurement_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    if is_negative_expected(labels):
        return
    circles = labels.get("circles") or labels.get("measurements")
    if not isinstance(circles, list) or not circles:
        errors.append(f"case[{index}].labels.circles or measurements must be a non-empty list")
        return
    for circle_index, item in enumerate(circles):
        item_label = f"case[{index}].labels.circles[{circle_index}]"
        geometry = item.get("circle") if isinstance(item, dict) and "circle" in item else item
        validate_circle_geometry(geometry, item_label, errors)
        if isinstance(item, dict):
            validate_positive_number(item.get("tolerancePx"), f"{item_label}.tolerancePx", errors)
            if "partial" in item and not isinstance(item.get("partial"), bool):
                errors.append(f"{item_label}.partial must be boolean")


def validate_geometric_fitting_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    if is_negative_expected(labels):
        return
    targets = labels.get("fitTargets") or labels.get("geometries") or labels.get("shapes")
    if targets is None and (labels.get("fitType") or labels.get("type")):
        targets = [labels]
    if not isinstance(targets, list) or not targets:
        errors.append(f"case[{index}].labels.fitTargets/geometries/shapes must be a non-empty list")
        return
    for target_index, target in enumerate(targets):
        target_label = f"case[{index}].labels.fitTargets[{target_index}]"
        if not isinstance(target, dict):
            errors.append(f"{target_label} must be an object")
            continue
        fit_type = str(target.get("fitType") or target.get("type") or target.get("shapeType") or "").lower()
        if fit_type not in {"line", "circle", "rectangle", "ellipse"}:
            errors.append(f"{target_label}.fitType must be line/circle/rectangle/ellipse")
            continue
        geometry = target.get("geometry") or target
        if fit_type == "line":
            validate_line_geometry(geometry, target_label, errors)
        elif fit_type == "circle":
            validate_circle_geometry(geometry, target_label, errors)
        elif fit_type == "rectangle":
            validate_bbox(geometry.get("bbox") if isinstance(geometry, dict) else geometry, target_label, errors)
        elif fit_type == "ellipse":
            if not isinstance(geometry, dict):
                errors.append(f"{target_label}.ellipse geometry must be an object")
            else:
                validate_point(geometry.get("center"), f"{target_label}.center", errors)
                validate_positive_number(geometry.get("majorAxisPx"), f"{target_label}.majorAxisPx", errors)
                validate_positive_number(geometry.get("minorAxisPx"), f"{target_label}.minorAxisPx", errors)
        validate_positive_number(target.get("residualTolerancePx", target.get("tolerancePx")), f"{target_label}.residualTolerancePx", errors)


def validate_template_matching_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    if is_negative_expected(labels):
        return
    expected = expected_result(labels)
    if expected and expected not in {"match", "found", "ok", "positive", "pass"}:
        errors.append(f"case[{index}].labels.expectedResult must be match/found/ok/positive/pass or a no-match negative")
    if "homography" in labels:
        validate_homography(labels.get("homography"), f"case[{index}].labels", errors)
        return
    if "pose" in labels:
        validate_pose(labels.get("pose"), f"case[{index}].labels", errors)
        return
    bbox = labels.get("bbox", labels.get("expectedLocation"))
    if bbox is not None:
        validate_bbox(bbox, f"case[{index}].labels", errors)
        return
    errors.append(f"case[{index}].labels must include homography, pose, or bbox/expectedLocation for positive matches")


def validate_shape_matching_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    if is_negative_expected(labels):
        return
    expected = expected_result(labels)
    if expected and expected not in {"match", "found", "ok", "positive", "pass"}:
        errors.append(f"case[{index}].labels.expectedResult must be match/found/ok/positive/pass or a no-match negative")
    if "pose" in labels:
        validate_pose(labels.get("pose"), f"case[{index}].labels", errors)
    elif "bbox" in labels:
        validate_bbox(labels.get("bbox"), f"case[{index}].labels", errors)
    else:
        errors.append(f"case[{index}].labels must include pose or bbox for positive shape matches")


def validate_shape_pose_match_case(operator: str, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    if is_negative_expected(labels):
        return
    expected = expected_result(labels)
    if expected and expected not in {"match", "found", "ok", "positive", "pass"}:
        errors.append(f"case[{index}].labels.expectedResult must be match/found/ok/positive/pass or a no-match negative")
    pose = labels.get("pose") or labels.get("multiscalePose")
    if pose is not None:
        validate_pose(pose, f"case[{index}].labels", errors)
        if operator == "PyramidShapeMatch" and isinstance(pose, dict) and "scale" not in pose:
            errors.append(f"case[{index}].labels.pose.scale is required for PyramidShapeMatch")
        return
    if "bbox" in labels:
        validate_bbox(labels.get("bbox"), f"case[{index}].labels", errors)
        return
    errors.append(f"case[{index}].labels must include pose/multiscalePose or bbox for positive {operator} cases")


def validate_feature_match_case(operator: str, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    if is_negative_expected(labels):
        return
    expected = expected_result(labels)
    if expected and expected not in {"match", "found", "ok", "positive", "pass"}:
        errors.append(f"case[{index}].labels.expectedResult must be match/found/ok/positive/pass or a low-feature/no-match negative")
    if "homography" in labels:
        validate_homography(labels.get("homography"), f"case[{index}].labels", errors)
    matches = labels.get("matchedKeypoints") or labels.get("keypointMatches")
    if matches is not None:
        validate_point_pairs(matches, f"case[{index}].labels.matchedKeypoints", errors)
    if "homography" not in labels and matches is None:
        errors.append(f"case[{index}].labels must include homography or matchedKeypoints/keypointMatches for positive {operator} cases")
    accepted = labels.get("acceptedMatches")
    if accepted is not None and (not isinstance(accepted, int) or isinstance(accepted, bool) or accepted <= 0):
        errors.append(f"case[{index}].labels.acceptedMatches must be a positive integer when present")


def validate_planar_matching_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    if is_negative_expected(labels):
        return
    if "homography" not in labels:
        errors.append(f"case[{index}].labels.homography is required for positive planar matches")
    else:
        validate_homography(labels.get("homography"), f"case[{index}].labels", errors)
    corners = labels.get("corners") or labels.get("templateCorners")
    if corners is not None:
        if not isinstance(corners, list) or len(corners) < 4:
            errors.append(f"case[{index}].labels.corners must include at least four points")
        else:
            for corner_index, corner in enumerate(corners):
                validate_point(corner, f"case[{index}].labels.corners[{corner_index}]", errors)
    if "cornerTolerancePx" in labels and (not numeric(labels.get("cornerTolerancePx")) or float(labels["cornerTolerancePx"]) <= 0):
        errors.append(f"case[{index}].labels.cornerTolerancePx must be positive")


def validate_local_deformable_matching_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    if is_negative_expected(labels):
        return
    control_points = labels.get("controlPoints") or labels.get("warpControlPoints")
    warp_field = labels.get("warpField") or labels.get("deformationField")
    if control_points is None and warp_field is None:
        errors.append(f"case[{index}].labels must include controlPoints/warpControlPoints or warpField/deformationField")
    if control_points is not None:
        validate_point_pairs(control_points, f"case[{index}].labels.controlPoints", errors)
    validate_label_path(root, warp_field, f"case[{index}].labels.warpField", errors)
    if "deformationTolerancePx" in labels and (
        not numeric(labels.get("deformationTolerancePx")) or float(labels["deformationTolerancePx"]) <= 0
    ):
        errors.append(f"case[{index}].labels.deformationTolerancePx must be positive")


def validate_edge_detection_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    edge_mask = labels.get("edgeMask", labels.get("mask"))
    polylines = labels.get("edgePolylines", labels.get("boundaries"))
    edge_count = labels.get("edgeCount")
    if edge_mask is None and polylines is None and edge_count is None:
        errors.append(f"case[{index}].labels must include edgeMask, edgePolylines/boundaries, or edgeCount")
    validate_label_path(root, edge_mask, f"case[{index}].labels.edgeMask", errors)
    if polylines is not None and (not isinstance(polylines, list) or not polylines):
        errors.append(f"case[{index}].labels.edgePolylines/boundaries must be a non-empty list")
    if edge_count is not None and (not isinstance(edge_count, int) or isinstance(edge_count, bool) or edge_count < 0):
        errors.append(f"case[{index}].labels.edgeCount must be a non-negative integer")
    if "tolerancePx" in labels and (not numeric(labels.get("tolerancePx")) or float(labels["tolerancePx"]) <= 0):
        errors.append(f"case[{index}].labels.tolerancePx must be positive")


def validate_semantic_segmentation_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    classes = labels.get("classes", labels.get("classIds"))
    if not isinstance(classes, list) or not classes:
        errors.append(f"case[{index}].labels.classes or classIds must be a non-empty list")
    mask = labels.get("mask", labels.get("segmentationMask"))
    class_masks = labels.get("classMasks")
    segments = labels.get("segments")
    if mask is None and class_masks is None and segments is None:
        errors.append(f"case[{index}].labels must include mask/segmentationMask, classMasks, or segments")
    validate_label_path(root, mask, f"case[{index}].labels.mask", errors)
    if class_masks is not None and not (
        isinstance(class_masks, dict) and class_masks or isinstance(class_masks, list) and class_masks
    ):
        errors.append(f"case[{index}].labels.classMasks must be a non-empty object or list")
    if segments is not None and (not isinstance(segments, list) or not segments):
        errors.append(f"case[{index}].labels.segments must be a non-empty list")


def validate_anomaly_detection_case(root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    labels = case.get("labels") if isinstance(case.get("labels"), dict) else {}
    image_label = str(labels.get("imageLabel") or case.get("label") or "").lower()
    if image_label not in {"ok", "good", "normal", "ng", "defect", "anomaly"}:
        errors.append(f"case[{index}].labels.imageLabel must be ok/good/normal/ng/defect/anomaly")
    if image_label in {"ng", "defect", "anomaly"}:
        if not (labels.get("anomalyType") or labels.get("defectType")):
            errors.append(f"case[{index}].labels.anomalyType or defectType is required for anomaly cases")
        regions = labels.get("regions") or labels.get("anomalyRegions")
        masks = labels.get("mask", labels.get("masks"))
        if regions is None and masks is None:
            errors.append(f"case[{index}].labels.regions/anomalyRegions or mask/masks is required for anomaly cases")
        if regions is not None:
            validate_regions(regions, f"case[{index}].labels.regions", errors)
        validate_label_path(root, masks, f"case[{index}].labels.mask", errors)


def validate_operator_case(operator: str, root: Path, case: dict[str, Any], index: int, errors: list[str]) -> None:
    validate_common_case(root, case, index, errors)
    if operator == "SurfaceDefectDetection":
        validate_surface_defect_case(root, case, index, errors)
    elif operator == "DeepLearning":
        validate_deep_learning_case(root, case, index, errors)
    elif operator == "CaliperTool":
        validate_caliper_case(root, case, index, errors)
    elif operator == "TemplateMatching":
        validate_template_matching_case(root, case, index, errors)
    elif operator == "EdgeDetection":
        validate_edge_detection_case(root, case, index, errors)
    elif operator == "SemanticSegmentation":
        validate_semantic_segmentation_case(root, case, index, errors)
    elif operator == "ShapeMatching":
        validate_shape_matching_case(root, case, index, errors)
    elif operator == "AnomalyDetection":
        validate_anomaly_detection_case(root, case, index, errors)
    elif operator in {"GradientShapeMatch", "PyramidShapeMatch"}:
        validate_shape_pose_match_case(operator, case, index, errors)
    elif operator in {"AkazeFeatureMatch", "OrbFeatureMatch"}:
        validate_feature_match_case(operator, case, index, errors)
    elif operator == "PlanarMatching":
        validate_planar_matching_case(root, case, index, errors)
    elif operator == "LocalDeformableMatching":
        validate_local_deformable_matching_case(root, case, index, errors)
    elif operator == "ArcCaliper":
        validate_arc_caliper_case(root, case, index, errors)
    elif operator == "ContourDetection":
        validate_contour_detection_case(root, case, index, errors)
    elif operator == "BlobAnalysis":
        validate_blob_analysis_case(root, case, index, errors)
    elif operator == "LineMeasurement":
        validate_line_measurement_case(root, case, index, errors)
    elif operator == "CircleMeasurement":
        validate_circle_measurement_case(root, case, index, errors)
    elif operator == "GeometricFitting":
        validate_geometric_fitting_case(root, case, index, errors)
    else:
        errors.append(f"{operator} is not wired for field ingest yet")


def validate_case_document(document: dict[str, Any], operator: str, errors: list[str]) -> dict[str, Any]:
    approval = document.get("dataApproval")
    if not isinstance(approval, dict):
        errors.append("case manifest dataApproval object is required")
        return {}
    if approval.get("redactionStatus") != "approved":
        errors.append("dataApproval.redactionStatus must be approved")
    basis = approval.get("license") or approval.get("dataProcessingBasis")
    if not isinstance(basis, str) or not basis.strip():
        errors.append("dataApproval.license or dataApproval.dataProcessingBasis is required")
    if approval.get("containsPersonalData") is not False:
        errors.append("dataApproval.containsPersonalData must be false")
    return approval


def assign_split(case_id: str) -> str:
    digest = hashlib.sha256(f"{SPLIT_SEED}:{case_id}".encode("utf-8")).hexdigest()
    fraction = int(digest[:16], 16) / float(16**16)
    if fraction < 0.60:
        return "train"
    if fraction < 0.80:
        return "validation"
    return "test"


def build_split(cases: list[dict[str, Any]], operator: str, case_list: Path, root: Path) -> dict[str, Any]:
    buckets = {"train": [], "validation": [], "test": []}
    records = []
    for case in sorted(cases, key=lambda item: str(item["caseId"])):
        case_id = str(case["caseId"])
        split = assign_split(case_id)
        buckets[split].append(case_id)
        records.append(
            {
                "caseId": case_id,
                "split": split,
                "label": case_label(case),
                "failureTaxonomy": taxonomy_values(case),
                "isFailureOrBoundary": is_failure_or_boundary(case),
            }
        )

    return {
        "schemaVersion": "2026-04-29.core20-hash-split.v1",
        "datasetId": proof_name(operator),
        "operator": operator,
        "status": "field-data-ready",
        "strategy": "hashed-case-id-60-20-20",
        "seed": SPLIT_SEED,
        "hashAlgorithm": "sha256",
        "caseIdPolicy": "Only hashed/de-identified case ids are stored. Raw customer paths and serial numbers are forbidden.",
        "sourceCaseList": {
            "rootEnv": "CLEARVISION_PRODUCTION_DATASET_ROOT",
            "relativeRoot": f"core20/{operator}/field_v1",
            "file": case_list.name,
            "checksumSha256": sha256_file(case_list),
        },
        "assignment": {
            "train": "hash_fraction >= 0.00 and < 0.60",
            "validation": "hash_fraction >= 0.60 and < 0.80",
            "test": "hash_fraction >= 0.80 and < 1.00",
        },
        "counts": {
            "train": len(buckets["train"]),
            "validation": len(buckets["validation"]),
            "test": len(buckets["test"]),
            "total": len(cases),
        },
        "train": buckets["train"],
        "validation": buckets["validation"],
        "test": buckets["test"],
        "caseRecords": records,
        "generatedAtUtc": utc_now(),
    }


def validate_split(split: dict[str, Any], errors: list[str]) -> None:
    train = set(split.get("train", []))
    validation = set(split.get("validation", []))
    test = set(split.get("test", []))
    if train & validation or train & test or validation & test:
        errors.append("hash split contains overlapping case ids")
    for name, values in (("train", train), ("validation", validation), ("test", test)):
        if not values:
            errors.append(f"hash split {name} bucket must not be empty")
    total = len(train | validation | test)
    if total != split.get("counts", {}).get("total"):
        errors.append("hash split total count mismatch")


def update_field_manifest(operator: str, case_list: Path, split: dict[str, Any], approval: dict[str, Any]) -> dict[str, Any]:
    manifest_path = field_manifest_path(operator)
    manifest = read_json(manifest_path)
    manifest["status"] = "executed"
    manifest["source"]["license"] = approval.get("license") or approval.get("dataProcessingBasis")
    manifest["source"]["checksumSha256"] = sha256_file(case_list)
    manifest["source"]["caseListFile"] = case_list.name
    manifest["source"]["caseCount"] = split["counts"]["total"]
    manifest["split"]["trainCount"] = split["counts"]["train"]
    manifest["split"]["validationCount"] = split["counts"]["validation"]
    manifest["split"]["testCount"] = split["counts"]["test"]
    manifest["privacy"]["containsPersonalData"] = False
    manifest["privacy"]["containsCustomerData"] = bool(approval.get("containsCustomerData", True))
    manifest["privacy"]["redactionNotes"] = "Field case list ingested with approved de-identification; only hashed ids and relative file checks are stored in repo artifacts."
    return manifest


def build_operator_baseline(
    operator: str,
    item: dict[str, Any],
    cases: list[dict[str, Any]],
    split: dict[str, Any],
    manifest: dict[str, Any],
    manifest_sha256: str | None = None,
) -> dict[str, Any]:
    failure_count = sum(1 for case in cases if is_failure_or_boundary(case))
    sanitized_cases = split["caseRecords"]
    accepted = (
        len(cases) >= MIN_CASES
        and failure_count >= MIN_FAILURE_BOUNDARY_CASES
        and split["counts"]["train"] > 0
        and split["counts"]["validation"] > 0
        and split["counts"]["test"] > 0
    )
    proof_status = "field-data-ready" if accepted else "field-data-incomplete"
    return {
        "EvidenceKind": "field-data-ingest",
        "Summary": {
            "GeneratedAtUtc": utc_now(),
            "Operator": operator,
            "DatasetId": proof_name(operator),
            "CaseCount": len(cases),
            "TrainCount": split["counts"]["train"],
            "ValidationCount": split["counts"]["validation"],
            "TestCount": split["counts"]["test"],
            "FailureBoundaryCaseCount": failure_count,
            "MinCaseCount": MIN_CASES,
            "MinFailureBoundaryCaseCount": MIN_FAILURE_BOUNDARY_CASES,
            "PrivacyLeakCount": 0,
            "RawPathLeakCount": 0,
            "Accepted": accepted,
            "ProofStatus": proof_status,
            "ProofLevel": "field-data-ready",
            "IndustrialStatus": "field data ingested; algorithm proof and site/line sign-off are still pending",
        },
        "Operators": [
            {
                "Operator": operator,
                "datasetId": proof_name(operator),
                "manifest": repo(field_manifest_path(operator)),
                "manifestSha256": manifest_sha256
                or hashlib.sha256(json.dumps(manifest, ensure_ascii=False, sort_keys=True).encode("utf-8")).hexdigest(),
                "splitSummary": {
                    "strategy": split["strategy"],
                    "seed": split["seed"],
                    "trainCount": split["counts"]["train"],
                    "validationCount": split["counts"]["validation"],
                    "testCount": split["counts"]["test"],
                    "noOverlap": True,
                    "caseListPath": repo(split_path(operator)),
                },
                "metrics": {
                    "primary": item["primaryMetric"],
                    "secondary": manifest["metrics"]["secondary"],
                    "thresholds": freeze_thresholds(item),
                },
                "thresholds": freeze_thresholds(item),
                "perCaseResults": sanitized_cases,
                "failureTaxonomy": item["boundaries"],
                "privacyLeakCount": 0,
                "rawPathLeakCount": 0,
                "accepted": accepted,
                "proofLevel": "field-data-ready",
                "proofStatus": proof_status,
                "industrialStatus": "field data ingested; algorithm proof and site/line sign-off are still pending",
            }
        ],
    }


def render_operator_report(baseline: dict[str, Any]) -> str:
    summary = baseline["Summary"]
    lines = [
        f"# {summary['Operator']} Field Dataset Ingest Baseline",
        "",
        f"GeneratedAtUtc: `{summary['GeneratedAtUtc']}`",
        "",
        "## Summary",
        "",
        f"- Cases: {summary['CaseCount']}",
        f"- Split: train={summary['TrainCount']}, validation={summary['ValidationCount']}, test={summary['TestCount']}",
        f"- Failure/boundary cases: {summary['FailureBoundaryCaseCount']}",
        f"- Privacy/raw-path leaks: {summary['PrivacyLeakCount']}/{summary['RawPathLeakCount']}",
        f"- Accepted data gate: {'Yes' if summary['Accepted'] else 'No'}",
        f"- Proof status: `{summary['ProofStatus']}`",
        "",
        "## Case Split",
        "",
        "| Split | Cases |",
        "|---|---:|",
        f"| train | {summary['TrainCount']} |",
        f"| validation | {summary['ValidationCount']} |",
        f"| test | {summary['TestCount']} |",
        "",
        "## Boundary",
        "",
        "- This ingest baseline proves data readiness only; operator accuracy proof still requires running the algorithm against the frozen test split.",
        "- Repo artifacts contain hashed/de-identified case ids and taxonomy labels only, not customer raw paths.",
        "",
    ]
    return "\n".join(lines)


def update_aggregate_baseline(operator: str, operator_row: dict[str, Any]) -> None:
    path = REPORT_DIR / "QualityFlywheel_core20_proof_baseline.json"
    aggregate = read_json(path)
    rows = aggregate.get("Operators", [])
    replaced = False
    for index, row in enumerate(rows):
        if row.get("Operator") == operator:
            rows[index] = operator_row
            replaced = True
            break
    if not replaced:
        rows.append(operator_row)
    accepted = sum(1 for row in rows if row.get("accepted") is True)
    blocked = sum(1 for row in rows if row.get("proofStatus") == "blocked-missing-field-data")
    incomplete = sum(1 for row in rows if row.get("proofStatus") == "field-data-incomplete")
    summary = aggregate.setdefault("Summary", {})
    summary["Accepted"] = accepted
    summary["BlockedMissingFieldData"] = blocked
    summary["FieldDataIncomplete"] = incomplete
    summary["FieldDataReady"] = sum(1 for row in rows if row.get("proofStatus") == "field-data-ready")
    summary["ProofGatePassed"] = accepted == len(CORE20_OPERATORS)
    summary["ProofGateInterpretation"] = (
        "All Core20 field datasets are ready for algorithm proof."
        if summary["ProofGatePassed"]
        else "Some field dataset proof rows are still blocked or incomplete; legacy baselines remain regression evidence only."
    )
    aggregate["Operators"] = rows
    write_json(path, aggregate)
    (REPORT_DIR / "QualityFlywheel_core20_proof_baseline.md").write_text(
        render_proof_report(aggregate), encoding="utf-8", newline="\n"
    )


def update_registry(operator: str, status: str) -> None:
    path = REPORT_DIR / "QualityFlywheel_core20_proof_registry.json"
    registry = read_json(path)
    for row in registry.get("operators", []):
        if row.get("operator") == operator:
            row["proofStatus"] = status
            row["proofLevel"] = "field-data-ready" if status == "field-data-ready" else "field-data-incomplete"
            row["industrialStatus"] = "field data ingested; real industrial validation is not complete"
            row["proofBaselineJson"] = repo(field_baseline_path(operator))
            row["proofReportMarkdown"] = repo(field_report_path(operator))
            break
    ready = sum(1 for row in registry.get("operators", []) if row.get("proofStatus") == "field-data-ready")
    blocked = sum(1 for row in registry.get("operators", []) if row.get("proofStatus") == "blocked-missing-field-data")
    incomplete = sum(1 for row in registry.get("operators", []) if row.get("proofStatus") == "field-data-incomplete")
    summary = registry.setdefault("summary", {})
    summary["fieldDatasetReadyCount"] = ready
    summary["fieldDatasetBlockedCount"] = blocked
    summary["fieldDatasetIncompleteCount"] = incomplete
    summary["realIndustrialValidationComplete"] = 0
    write_json(path, registry)
    (REPORT_DIR / "QualityFlywheel_core20_proof_registry.md").write_text(
        render_registry_markdown(registry), encoding="utf-8", newline="\n"
    )


def ingest_operator(operator: str, dataset_root: Path | None, dry_run: bool, allow_missing_data: bool) -> int:
    item = operator_item(operator)
    if dataset_root is None:
        if allow_missing_data:
            print(f"[{operator}] CLEARVISION_PRODUCTION_DATASET_ROOT is not set; leaving proof status blocked.")
            return 0
        raise IngestError("CLEARVISION_PRODUCTION_DATASET_ROOT is not set; pass --dataset-root or --allow-missing-data")

    root = field_root(dataset_root, operator)
    if not root.exists():
        if allow_missing_data:
            print(f"[{operator}] field root missing: {root}; leaving proof status blocked.")
            return 0
        raise IngestError(f"{operator} field root does not exist: {root}")
    case_list = find_case_list(root)
    if case_list is None:
        if allow_missing_data:
            print(f"[{operator}] no case list found under {root}; leaving proof status blocked.")
            return 0
        raise IngestError(f"{operator} case list not found; expected one of {', '.join(CASE_LIST_CANDIDATES)}")

    cases, document = load_cases(case_list, operator)
    errors: list[str] = []
    approval = validate_case_document(document, operator, errors)
    if len(cases) < MIN_CASES:
        errors.append(f"{operator} requires at least {MIN_CASES} cases for field-data-ready status")
    seen: set[str] = set()
    for index, case in enumerate(cases):
        if not isinstance(case, dict):
            errors.append(f"case[{index}] must be an object")
            continue
        case_id = case.get("caseId")
        if isinstance(case_id, str):
            if case_id in seen:
                errors.append(f"duplicate caseId: {case_id}")
            seen.add(case_id)
        validate_operator_case(operator, root, case, index, errors)

    failure_count = sum(1 for case in cases if isinstance(case, dict) and is_failure_or_boundary(case))
    if failure_count < MIN_FAILURE_BOUNDARY_CASES:
        errors.append(f"{operator} requires at least {MIN_FAILURE_BOUNDARY_CASES} failure/boundary cases")

    split = build_split(cases, operator, case_list, root)
    validate_split(split, errors)

    if errors:
        raise IngestError("\n".join(errors))

    manifest = update_field_manifest(operator, case_list, split, approval)
    if dry_run:
        print(f"[{operator}] dry-run valid: cases={len(cases)} train={split['counts']['train']} validation={split['counts']['validation']} test={split['counts']['test']}")
        return 0

    write_json(split_path(operator), split)
    write_json(field_manifest_path(operator), manifest)
    baseline = build_operator_baseline(operator, item, cases, split, manifest, sha256_file(field_manifest_path(operator)))
    operator_row = baseline["Operators"][0]
    write_json(field_baseline_path(operator), baseline)
    field_report_path(operator).write_text(render_operator_report(baseline), encoding="utf-8", newline="\n")
    update_aggregate_baseline(operator, operator_row)
    update_registry(operator, operator_row["proofStatus"])
    print(
        f"[{operator}] ingested field cases: total={len(cases)} "
        f"train={split['counts']['train']} validation={split['counts']['validation']} test={split['counts']['test']} "
        f"status={operator_row['proofStatus']}"
    )
    return 0


def validate_config_only(operators: list[str]) -> int:
    missing = []
    for operator in operators:
        operator_item(operator)
        for path in (field_manifest_path(operator), split_path(operator)):
            if not path.exists():
                missing.append(repo(path))
    if missing:
        raise IngestError("missing generated Core20 proof assets:\n" + "\n".join(missing))
    print(f"field ingest config valid: operators={','.join(operators)}")
    return 0


def parse_operators(args: argparse.Namespace) -> list[str]:
    values: list[str] = []
    if args.all_representatives:
        values.extend(REPRESENTATIVE_OPERATORS)
    if args.operator:
        values.extend(args.operator)
    if args.operators:
        values.extend(args.operators)
    if not values:
        values.extend(REPRESENTATIVE_OPERATORS)
    unique: list[str] = []
    for value in values:
        if value not in unique:
            unique.append(value)
    for value in unique:
        if value not in INGEST_ENABLED_OPERATORS:
            raise IngestError(
                f"{value} is not enabled for this ingest tool yet; enabled operators: {', '.join(INGEST_ENABLED_OPERATORS)}"
            )
    return unique


def main() -> int:
    parser = argparse.ArgumentParser(description="Ingest approved de-identified Core20 field dataset case manifests.")
    parser.add_argument("--operator", action="append", help="Enabled Core20 operator to ingest. Repeatable.")
    parser.add_argument("--operators", nargs="*", help="Enabled Core20 operators to ingest.")
    parser.add_argument("--all-representatives", action="store_true", help="Ingest all currently wired representative Core20 operators.")
    parser.add_argument("--dataset-root", help="Override CLEARVISION_PRODUCTION_DATASET_ROOT.")
    parser.add_argument("--allow-missing-data", action="store_true", help="Return success when the external field dataset is not available yet.")
    parser.add_argument("--validate-config-only", action="store_true", help="Validate generated repo-side ingest configuration only.")
    parser.add_argument("--dry-run", action="store_true", help="Validate the external case list without writing split/baseline files.")
    args = parser.parse_args()

    try:
        operators = parse_operators(args)
        if args.validate_config_only:
            return validate_config_only(operators)
        dataset_root = resolve_dataset_root(args.dataset_root)
        for operator in operators:
            ingest_operator(operator, dataset_root, args.dry_run, args.allow_missing_data)
        return 0
    except IngestError as error:
        print(f"error: {error}")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
