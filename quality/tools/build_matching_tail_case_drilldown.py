from __future__ import annotations

import json
import math
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from build_matching_tail_error_reduction_report import (
    REPORTS,
    CENTER_GATE_MAX_MEAN_REPROJ,
    CENTER_GATE_MAX_REPROJ,
    CENTER_GATE_MIN_AREA_RATIO,
    CENTER_GATE_MAX_AREA_RATIO,
    CENTER_GATE_MIN_INLIER_RATIO,
    classify_tail,
    center_gate_candidate,
    finite_number,
    label_for,
    read_json,
    repo,
    write_json,
    write_text,
)


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
OUTPUT_JSON = REPORT_DIR / "QualityFlywheel_matching_tail_case_drilldown_v2.json"
OUTPUT_MD = REPORT_DIR / "QualityFlywheel_matching_tail_case_drilldown_v2.md"


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def safe_float(value: Any, fallback: float) -> float:
    number = finite_number(value)
    return fallback if number is None else number


def actionability_score(case: dict[str, Any]) -> float:
    inlier_ratio = safe_float(case.get("InlierRatio"), 0)
    mean_reproj = safe_float(case.get("MeanReprojectionError"), 10)
    max_reproj = safe_float(case.get("MaxReprojectionError"), 30)
    area_ratio = max(safe_float(case.get("AreaRatio"), 0), 1e-6)
    corners_inside = int(case.get("CornersInsideCount") or 0)
    position_error = safe_float(case.get("PositionErrorPx"), 0)
    area_penalty = abs(math.log(area_ratio))
    return round(
        (inlier_ratio * 100.0)
        - (mean_reproj * 12.0)
        - (max_reproj * 2.0)
        - (area_penalty * 12.0)
        - (corners_inside * 4.0)
        + min(position_error, 500.0) * 0.03,
        6,
    )


def recommended_action(bucket: str, case: dict[str, Any]) -> str:
    if center_gate_candidate(case):
        return "try-geometry-candidate-selection"
    if bucket == "partial_viewpoint_crop":
        return "try-crop-aware-quad-validity"
    if bucket == "projected_area_drift":
        return "try-area-ratio-prior"
    if bucket == "reprojection_outlier":
        return "try-local-consistency-filter"
    if bucket == "insufficient_correspondences":
        return "detector-coverage-not-homography-selection"
    if bucket == "illumination_residual":
        return "photometric-descriptor-profile"
    return "inspect-case"


def build_case_rows() -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for path in REPORTS:
        if not path.exists():
            continue
        document = read_json(path)
        label = label_for(document, path)
        summary = document["Summary"]
        min_inliers = int(summary.get("MinInliers") or 6)
        for case in document.get("Cases", []):
            if case.get("Passed") is True:
                continue
            bucket = classify_tail(case, min_inliers)
            row = {
                "caseId": case.get("CaseId"),
                "operatorLabel": label,
                "operator": summary.get("Operator"),
                "detectorType": summary.get("DetectorType"),
                "sourceReport": repo(path),
                "sequenceType": case.get("SequenceType"),
                "pair": case.get("Pair"),
                "bucket": bucket,
                "actionabilityScore": actionability_score(case),
                "centerGateCandidate": center_gate_candidate(case),
                "recommendedAction": recommended_action(bucket, case),
                "positionErrorPx": case.get("PositionErrorPx"),
                "meanCornerErrorPx": case.get("MeanCornerErrorPx"),
                "maxCornerErrorPx": case.get("MaxCornerErrorPx"),
                "inlierRatio": case.get("InlierRatio"),
                "inliers": case.get("Inliers"),
                "totalMatches": case.get("TotalMatches"),
                "meanReprojectionError": case.get("MeanReprojectionError"),
                "maxReprojectionError": case.get("MaxReprojectionError"),
                "areaRatio": case.get("AreaRatio"),
                "cornersInsideCount": case.get("CornersInsideCount"),
                "projectedCenterInside": case.get("ProjectedCenterInside"),
                "homographyFailureReason": case.get("HomographyFailureReason"),
                "failure": case.get("Failure"),
            }
            rows.append(row)
    return rows


def build_cross_case(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        grouped[str(row["caseId"])].append(row)
    cross_rows = []
    for case_id, items in grouped.items():
        center_gate_count = sum(1 for item in items if item["centerGateCandidate"])
        cross_rows.append(
            {
                "caseId": case_id,
                "operatorCount": len(items),
                "centerGateCount": center_gate_count,
                "maxActionabilityScore": max(item["actionabilityScore"] for item in items),
                "maxPositionErrorPx": max(safe_float(item.get("positionErrorPx"), 0) for item in items),
                "buckets": sorted(set(str(item["bucket"]) for item in items)),
                "operators": [
                    {
                        "operatorLabel": item["operatorLabel"],
                        "bucket": item["bucket"],
                        "actionabilityScore": item["actionabilityScore"],
                        "positionErrorPx": item["positionErrorPx"],
                        "inlierRatio": item["inlierRatio"],
                        "meanReprojectionError": item["meanReprojectionError"],
                        "maxReprojectionError": item["maxReprojectionError"],
                        "areaRatio": item["areaRatio"],
                        "cornersInsideCount": item["cornersInsideCount"],
                        "recommendedAction": item["recommendedAction"],
                    }
                    for item in sorted(items, key=lambda value: value["actionabilityScore"], reverse=True)
                ],
            }
        )
    return sorted(
        cross_rows,
        key=lambda value: (value["centerGateCount"], value["maxActionabilityScore"], value["operatorCount"]),
        reverse=True,
    )


def build_document() -> dict[str, Any]:
    rows = build_case_rows()
    center_gate = sorted(
        [row for row in rows if row["centerGateCandidate"]],
        key=lambda value: value["actionabilityScore"],
        reverse=True,
    )
    cross_case = build_cross_case(rows)
    top_case_ids = []
    for row in center_gate:
        case_id = row["caseId"]
        if case_id not in top_case_ids:
            top_case_ids.append(case_id)
        if len(top_case_ids) >= 20:
            break
    return {
        "schemaVersion": "2026-04-30.matching-tail-case-drilldown.v2",
        "generatedAtUtc": utc_now(),
        "claimBoundary": "Public HPatches tail-case drilldown only; use as candidate triage, not production signoff.",
        "thresholds": {
            "centerGateMinInlierRatio": CENTER_GATE_MIN_INLIER_RATIO,
            "centerGateMaxMeanReprojectionError": CENTER_GATE_MAX_MEAN_REPROJ,
            "centerGateMaxReprojectionError": CENTER_GATE_MAX_REPROJ,
            "centerGateAreaRatioRange": [CENTER_GATE_MIN_AREA_RATIO, CENTER_GATE_MAX_AREA_RATIO],
        },
        "summary": {
            "failedCaseRows": len(rows),
            "centerGateCandidateRows": len(center_gate),
            "crossCaseRows": len(cross_case),
            "recommendedSmallGateCaseIds": top_case_ids,
        },
        "centerGateCandidates": center_gate,
        "crossCaseGroups": cross_case[:40],
        "allFailedRows": sorted(rows, key=lambda value: value["actionabilityScore"], reverse=True),
    }


def fmt(value: Any) -> str:
    if isinstance(value, float):
        return f"{value:.3f}".rstrip("0").rstrip(".")
    return "-" if value is None else str(value)


def render_markdown(document: dict[str, Any]) -> str:
    summary = document["summary"]
    case_ids = ",".join(summary["recommendedSmallGateCaseIds"])
    lines = [
        "# Matching Tail Case Drilldown v2",
        "",
        f"GeneratedAtUtc: `{document['generatedAtUtc']}`",
        f"ClaimBoundary: `{document['claimBoundary']}`",
        "",
        "## Summary",
        "",
        "| Metric | Value |",
        "|---|---:|",
        f"| Failed case rows | {summary['failedCaseRows']} |",
        f"| Center-gate candidate rows | {summary['centerGateCandidateRows']} |",
        f"| Cross-case groups | {summary['crossCaseRows']} |",
        "",
        "## Recommended Small Gate",
        "",
        f"`{case_ids}`",
        "",
        "## Center-Gate Candidates",
        "",
        "| Rank | Case | Operator | Score | Bucket | Pos px | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Action |",
        "|---:|---|---|---:|---|---:|---:|---:|---:|---:|---:|---|",
    ]
    for index, row in enumerate(document["centerGateCandidates"][:40], start=1):
        lines.append(
            f"| {index} | {row['caseId']} | {row['operatorLabel']} | {fmt(row['actionabilityScore'])} | "
            f"{row['bucket']} | {fmt(row['positionErrorPx'])} | {fmt(row['inlierRatio'])} | "
            f"{fmt(row['meanReprojectionError'])} | {fmt(row['maxReprojectionError'])} | "
            f"{fmt(row['areaRatio'])} | {fmt(row['cornersInsideCount'])} | {row['recommendedAction']} |"
        )
    lines.extend(
        [
            "",
            "## Cross-Case Groups",
            "",
            "| Rank | Case | Operators failed | Center-gate rows | Max score | Max pos px | Buckets |",
            "|---:|---|---:|---:|---:|---:|---|",
        ]
    )
    for index, row in enumerate(document["crossCaseGroups"][:25], start=1):
        lines.append(
            f"| {index} | {row['caseId']} | {row['operatorCount']} | {row['centerGateCount']} | "
            f"{fmt(row['maxActionabilityScore'])} | {fmt(row['maxPositionErrorPx'])} | "
            f"{', '.join(row['buckets'])} |"
        )
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    document = build_document()
    write_json(OUTPUT_JSON, document)
    write_text(OUTPUT_MD, render_markdown(document))
    print(
        "matching tail case drilldown complete: "
        f"centerGate={document['summary']['centerGateCandidateRows']}, "
        f"output={repo(OUTPUT_JSON)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
