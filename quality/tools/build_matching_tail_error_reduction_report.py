from __future__ import annotations

import json
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from statistics import mean
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"

AKAZE_REPORT = REPORT_DIR / "AkazeFeatureMatch_hpatches_candidate_v4.json"
ORB_REPORT = REPORT_DIR / "OrbFeatureMatch_hpatches_candidate_v4.json"
PLANAR_ORB_REPORT = REPORT_DIR / "PlanarMatching_hpatches_baseline.json"
PLANAR_AKAZE_REPORT = REPORT_DIR / "PlanarMatching_hpatches_akaze_baseline.json"
BACKLOG_REPORT = REPORT_DIR / "QualityFlywheel_matching_failure_backlog_v1.json"

OUTPUT_JSON = REPORT_DIR / "QualityFlywheel_matching_tail_error_reduction_v2.json"
OUTPUT_MD = REPORT_DIR / "QualityFlywheel_matching_tail_error_reduction_v2.md"

REPORTS = (
    AKAZE_REPORT,
    ORB_REPORT,
    PLANAR_ORB_REPORT,
    PLANAR_AKAZE_REPORT,
)

POSITION_TAIL_PX = 60.0
CENTER_GATE_MIN_INLIER_RATIO = 0.9
CENTER_GATE_MAX_MEAN_REPROJ = 1.5
CENTER_GATE_MAX_REPROJ = 6.0
CENTER_GATE_MIN_AREA_RATIO = 0.5
CENTER_GATE_MAX_AREA_RATIO = 2.5


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def repo(path: Path) -> str:
    return path.relative_to(REPO_ROOT).as_posix()


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8-sig") as handle:
        return json.load(handle)


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def write_text(path: Path, value: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value, encoding="utf-8", newline="\n")


def finite_number(value: Any) -> float | None:
    try:
        number = float(value)
    except (TypeError, ValueError):
        return None
    return number if number == number and abs(number) != float("inf") else None


def percentile(values: list[float], q: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    index = min(max(int(len(ordered) * q + 0.999999) - 1, 0), len(ordered) - 1)
    return ordered[index]


def label_for(document: dict[str, Any], path: Path) -> str:
    summary = document["Summary"]
    operator = summary.get("Operator", path.stem)
    detector = summary.get("DetectorType")
    if operator == "PlanarMatching" and detector:
        return f"{operator}({detector})"
    return str(operator)


def classify_tail(case: dict[str, Any], min_inliers: int) -> str:
    reason = str(case.get("HomographyFailureReason") or case.get("Failure") or "")
    sequence_type = str(case.get("SequenceType") or "")
    inliers = int(case.get("Inliers") or 0)
    total_matches = int(case.get("TotalMatches") or 0)
    corners_inside = int(case.get("CornersInsideCount") or 0)
    center_inside = case.get("ProjectedCenterInside") is True
    area_ratio = finite_number(case.get("AreaRatio"))
    mean_reproj = finite_number(case.get("MeanReprojectionError"))
    max_reproj = finite_number(case.get("MaxReprojectionError"))

    if case.get("Passed") is True:
        return "passed"
    if "At least four" in reason or "Insufficient" in reason or inliers < min_inliers or total_matches < min_inliers:
        return "insufficient_correspondences"
    if sequence_type == "illumination":
        return "illumination_residual"
    if mean_reproj is not None and mean_reproj > 3.0 or max_reproj is not None and max_reproj > 8.0:
        return "reprojection_outlier"
    if area_ratio is not None and (area_ratio < 0.5 or area_ratio > 2.0):
        return "projected_area_drift"
    if center_inside and corners_inside <= 1:
        return "extreme_viewpoint_crop"
    if center_inside and corners_inside in {2, 3}:
        return "partial_viewpoint_crop"
    return "localization_tail"


def center_gate_candidate(case: dict[str, Any]) -> bool:
    inlier_ratio = finite_number(case.get("InlierRatio"))
    mean_reproj = finite_number(case.get("MeanReprojectionError"))
    max_reproj = finite_number(case.get("MaxReprojectionError"))
    area_ratio = finite_number(case.get("AreaRatio"))
    corners_inside = int(case.get("CornersInsideCount") or 0)
    return (
        case.get("Passed") is not True
        and str(case.get("SequenceType")) == "viewpoint"
        and case.get("ProjectedCenterInside") is True
        and corners_inside <= 1
        and inlier_ratio is not None
        and inlier_ratio >= CENTER_GATE_MIN_INLIER_RATIO
        and mean_reproj is not None
        and mean_reproj <= CENTER_GATE_MAX_MEAN_REPROJ
        and max_reproj is not None
        and max_reproj <= CENTER_GATE_MAX_REPROJ
        and area_ratio is not None
        and CENTER_GATE_MIN_AREA_RATIO <= area_ratio <= CENTER_GATE_MAX_AREA_RATIO
    )


def summarize_cases(cases: list[dict[str, Any]]) -> dict[str, Any]:
    position_errors = [value for value in (finite_number(case.get("PositionErrorPx")) for case in cases) if value is not None]
    max_corner_errors = [value for value in (finite_number(case.get("MaxCornerErrorPx")) for case in cases) if value is not None]
    inlier_ratios = [value for value in (finite_number(case.get("InlierRatio")) for case in cases) if value is not None]
    mean_reprojs = [value for value in (finite_number(case.get("MeanReprojectionError")) for case in cases) if value is not None]
    max_reprojs = [value for value in (finite_number(case.get("MaxReprojectionError")) for case in cases) if value is not None]
    area_ratios = [value for value in (finite_number(case.get("AreaRatio")) for case in cases) if value is not None]
    corners_inside = [int(case.get("CornersInsideCount") or 0) for case in cases]
    return {
        "caseCount": len(cases),
        "meanPositionErrorPx": round(mean(position_errors), 6) if position_errors else None,
        "p95PositionErrorPx": round(percentile(position_errors, 0.95), 6) if position_errors else None,
        "maxPositionErrorPx": round(max(position_errors), 6) if position_errors else None,
        "p95MaxCornerErrorPx": round(percentile(max_corner_errors, 0.95), 6) if max_corner_errors else None,
        "meanInlierRatio": round(mean(inlier_ratios), 6) if inlier_ratios else None,
        "meanReprojectionError": round(mean(mean_reprojs), 6) if mean_reprojs else None,
        "maxReprojectionError": round(max(max_reprojs), 6) if max_reprojs else None,
        "meanAreaRatio": round(mean(area_ratios), 6) if area_ratios else None,
        "meanCornersInsideCount": round(mean(corners_inside), 6) if corners_inside else None,
    }


def report_row(path: Path) -> dict[str, Any]:
    document = read_json(path)
    summary = document["Summary"]
    label = label_for(document, path)
    cases = list(document.get("Cases", []))
    min_inliers = int(summary.get("MinInliers") or 6)
    failed = [case for case in cases if case.get("Passed") is not True]
    viewpoint = [case for case in cases if case.get("SequenceType") == "viewpoint"]
    viewpoint_failed = [case for case in viewpoint if case.get("Passed") is not True]
    large_viewpoint_failures = [
        case for case in viewpoint_failed if (finite_number(case.get("PositionErrorPx")) or 0) >= POSITION_TAIL_PX
    ]
    buckets: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for case in cases:
        buckets[classify_tail(case, min_inliers)].append(case)

    tail_buckets = []
    for bucket, bucket_cases in sorted(buckets.items()):
        if bucket == "passed":
            continue
        worst = sorted(
            bucket_cases,
            key=lambda item: finite_number(item.get("PositionErrorPx")) or -1,
            reverse=True,
        )[:5]
        bucket_summary = summarize_cases(bucket_cases)
        bucket_summary.update(
            {
                "bucket": bucket,
                "samples": [
                    {
                        "caseId": item.get("CaseId"),
                        "sequenceType": item.get("SequenceType"),
                        "positionErrorPx": item.get("PositionErrorPx"),
                        "inlierRatio": item.get("InlierRatio"),
                        "meanReprojectionError": item.get("MeanReprojectionError"),
                        "maxReprojectionError": item.get("MaxReprojectionError"),
                        "areaRatio": item.get("AreaRatio"),
                        "cornersInsideCount": item.get("CornersInsideCount"),
                    }
                    for item in worst
                ],
            }
        )
        tail_buckets.append(bucket_summary)

    center_gate_cases = [case for case in failed if center_gate_candidate(case)]
    return {
        "label": label,
        "operator": summary.get("Operator"),
        "detectorType": summary.get("DetectorType"),
        "sourceReport": repo(path),
        "caseCount": summary.get("CaseCount", len(cases)),
        "passed": summary.get("Passed"),
        "failed": summary.get("Failed"),
        "passRate": summary.get("PassRate"),
        "meanPositionErrorPx": summary.get("MeanPositionErrorPx"),
        "p95PositionErrorPx": summary.get("P95PositionErrorPx"),
        "p95CornerErrorPx": summary.get("P95CornerErrorPx"),
        "viewpointCaseCount": len(viewpoint),
        "viewpointFailedCount": len(viewpoint_failed),
        "largeViewpointFailureCount": len(large_viewpoint_failures),
        "centerGateCandidateCount": len(center_gate_cases),
        "tailBucketCounts": dict(Counter(classify_tail(case, min_inliers) for case in failed)),
        "tailBuckets": tail_buckets,
        "tailSummary": summarize_cases(failed),
    }


def build_document() -> dict[str, Any]:
    existing_reports = [path for path in REPORTS if path.exists()]
    rows = [report_row(path) for path in existing_reports]
    backlog = read_json(BACKLOG_REPORT) if BACKLOG_REPORT.exists() else {}
    has_corner_evidence = bool(rows) and all(row.get("p95CornerErrorPx") is not None for row in rows)
    recommended_next_actions = [
        "Use the populated P95CornerErrorPx and bucket-level max-corner evidence to evaluate replay-safe profile candidates.",
        "Treat extreme_viewpoint_crop as geometry-tail triage first; current center-gate candidates need replay regression checks before any relaxed pass gate.",
        "Keep replay regression at zero before promoting stricter ratio, looser RANSAC, or multi-hypothesis homography profiles.",
    ] if has_corner_evidence else [
        "Run the updated HPatchesFeatureMatchDatasetRunner for the selected candidate profiles to populate P95CornerErrorPx.",
        "Treat extreme_viewpoint_crop as geometry-tail triage first; current center-gate candidates still need corner-error evidence before any relaxed pass gate.",
        "Keep replay regression at zero before promoting stricter ratio, looser RANSAC, or multi-hypothesis homography profiles.",
    ]
    return {
        "schemaVersion": "2026-04-30.matching-tail-error-reduction.v2",
        "generatedAtUtc": utc_now(),
        "claimBoundary": "Public HPatches tail-error triage only; this is not production-line signoff.",
        "sourceReports": [row["sourceReport"] for row in rows] + ([repo(BACKLOG_REPORT)] if BACKLOG_REPORT.exists() else []),
        "thresholds": {
            "positionTailPx": POSITION_TAIL_PX,
            "centerGateMinInlierRatio": CENTER_GATE_MIN_INLIER_RATIO,
            "centerGateMaxMeanReprojectionError": CENTER_GATE_MAX_MEAN_REPROJ,
            "centerGateMaxReprojectionError": CENTER_GATE_MAX_REPROJ,
            "centerGateAreaRatioRange": [CENTER_GATE_MIN_AREA_RATIO, CENTER_GATE_MAX_AREA_RATIO],
        },
        "backlogSummary": backlog.get("summary", {}),
        "rows": rows,
        "recommendedNextActions": recommended_next_actions,
    }


def fmt(value: Any) -> str:
    if isinstance(value, (int, float)):
        return f"{float(value):.3f}".rstrip("0").rstrip(".")
    return "-" if value is None else str(value)


def render_markdown(document: dict[str, Any]) -> str:
    lines = [
        "# Matching Tail Error Reduction v2",
        "",
        f"GeneratedAtUtc: `{document['generatedAtUtc']}`",
        f"ClaimBoundary: `{document['claimBoundary']}`",
        "",
        "## Operator Tail Summary",
        "",
        "| Candidate | HPatches pass | P95 position | P95 corner | Viewpoint failures | Large-viewpoint failures | Center-gate candidates |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ]
    for row in document["rows"]:
        lines.append(
            f"| {row['label']} | {row['passed']}/{row['caseCount']} ({row['passRate']}) | "
            f"{fmt(row['p95PositionErrorPx'])} | {fmt(row.get('p95CornerErrorPx'))} | "
            f"{row['viewpointFailedCount']}/{row['viewpointCaseCount']} | "
            f"{row['largeViewpointFailureCount']} | {row['centerGateCandidateCount']} |"
        )

    lines.extend(["", "## Tail Buckets", ""])
    for row in document["rows"]:
        lines.extend(
            [
                f"### {row['label']}",
                "",
                "| Bucket | Cases | P95 position | Max position | P95 max corner | Mean inlier ratio | Mean reproj | Max reproj | Mean area ratio | Mean corners inside |",
                "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
            ]
        )
        for bucket in row["tailBuckets"]:
            lines.append(
                f"| {bucket['bucket']} | {bucket['caseCount']} | {fmt(bucket['p95PositionErrorPx'])} | "
                f"{fmt(bucket['maxPositionErrorPx'])} | {fmt(bucket.get('p95MaxCornerErrorPx'))} | {fmt(bucket['meanInlierRatio'])} | "
                f"{fmt(bucket['meanReprojectionError'])} | {fmt(bucket['maxReprojectionError'])} | "
                f"{fmt(bucket['meanAreaRatio'])} | {fmt(bucket['meanCornersInsideCount'])} |"
            )
        lines.append("")

    lines.extend(
        [
            "## Next Actions",
            "",
        ]
    )
    lines.extend(f"- {item}" for item in document["recommendedNextActions"])
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    missing = [repo(path) for path in REPORTS[:2] if not path.exists()]
    if missing:
        raise SystemExit("error: missing required report(s): " + ", ".join(missing))
    document = build_document()
    write_json(OUTPUT_JSON, document)
    write_text(OUTPUT_MD, render_markdown(document))
    print(f"matching tail error reduction report complete: output={repo(OUTPUT_JSON)}, rows={len(document['rows'])}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
