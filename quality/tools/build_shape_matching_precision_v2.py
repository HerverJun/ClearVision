from __future__ import annotations

import argparse
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"

OUTPUT_JSON = REPORT_DIR / "QualityFlywheel_shape_matching_precision_v2.json"
OUTPUT_MD = REPORT_DIR / "QualityFlywheel_shape_matching_precision_v2.md"

TEMPLATE_PROJECT = "quality/tools/TemplateMatchingHomographyBridgeRunner/TemplateMatchingHomographyBridgeRunner.csproj"
SHAPE_PROJECT = "quality/tools/ShapeMatchingGeometricDatasetRunner/ShapeMatchingGeometricDatasetRunner.csproj"

TEMPLATE_JSON = REPORT_DIR / "TemplateMatching_public_bridge_candidate_replay_v2.json"
TEMPLATE_MD = REPORT_DIR / "TemplateMatching_public_bridge_candidate_replay_v2.md"
SHAPE_JSON = REPORT_DIR / "ShapeMatching_geometric_dataset_candidate_replay_v2.json"
SHAPE_MD = REPORT_DIR / "ShapeMatching_geometric_dataset_candidate_replay_v2.md"
GRADIENT_JSON = REPORT_DIR / "GradientShapeMatch_baseline.json"
PYRAMID_JSON = REPORT_DIR / "PyramidShapeMatch_contract_baseline.json"


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def repo(path: Path) -> str:
    return path.relative_to(REPO_ROOT).as_posix()


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8-sig") as handle:
        value = json.load(handle)
    if not isinstance(value, dict):
        raise ValueError(f"JSON root must be an object: {path}")
    return value


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def write_text(path: Path, value: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value, encoding="utf-8", newline="\n")


def as_float(value: Any) -> float | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, (int, float)):
        return float(value)
    return None


def round_value(value: float | None, digits: int = 6) -> float | None:
    if value is None:
        return None
    return round(value, digits)


def percentile(values: list[float], q: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    if len(ordered) == 1:
        return ordered[0]
    index = (len(ordered) - 1) * q
    lower = int(index)
    upper = min(lower + 1, len(ordered) - 1)
    fraction = index - lower
    return ordered[lower] + ((ordered[upper] - ordered[lower]) * fraction)


def mean(values: list[float]) -> float | None:
    return sum(values) / len(values) if values else None


def first_float(values: dict[str, Any], *keys: str) -> float | None:
    for key in keys:
        value = as_float(values.get(key))
        if value is not None:
            return value
    return None


def pass_rate(passed: int, total: int) -> float | None:
    return round(passed / total, 6) if total else None


def case_metrics(case: dict[str, Any]) -> dict[str, Any]:
    metrics = case.get("Metrics")
    merged = dict(case)
    if isinstance(metrics, dict):
        merged.update(metrics)
    return merged


def get_cases(payload: dict[str, Any]) -> list[dict[str, Any]]:
    cases = payload.get("Cases")
    return [case for case in cases if isinstance(case, dict)] if isinstance(cases, list) else []


def is_passed(case: dict[str, Any]) -> bool:
    return bool(case.get("Passed") if "Passed" in case else case.get("passed"))


def scenario_name(case: dict[str, Any]) -> str:
    return str(case.get("Scenario") or case.get("Sequence") or "unspecified")


def summarize_scenarios(cases: list[dict[str, Any]]) -> list[dict[str, Any]]:
    grouped: dict[str, list[dict[str, Any]]] = {}
    for case in cases:
        grouped.setdefault(scenario_name(case), []).append(case)

    rows: list[dict[str, Any]] = []
    for name in sorted(grouped):
        bucket = grouped[name]
        passed = sum(1 for case in bucket if is_passed(case))
        position_errors = []
        for case in bucket:
            metrics = case_metrics(case)
            position = first_float(metrics, "PositionErrorPx", "MeanPositionErrorPx")
            if position is not None:
                position_errors.append(position)
        rows.append(
            {
                "scenario": name,
                "caseCount": len(bucket),
                "passed": passed,
                "failed": len(bucket) - passed,
                "passRate": pass_rate(passed, len(bucket)),
                "meanPositionErrorPx": round_value(mean(position_errors)),
                "p95PositionErrorPx": round_value(percentile(position_errors, 0.95)),
            }
        )
    return rows


def occlusion_sensitivity(cases: list[dict[str, Any]]) -> dict[str, Any]:
    occlusion_cases = [
        case
        for case in cases
        if any(token in scenario_name(case).lower() for token in ("occlusion", "occluded", "partial"))
    ]
    if not occlusion_cases:
        return {
            "caseCount": 0,
            "passRate": None,
            "deltaFromOverallPassRate": None,
            "coverage": "not-covered",
        }

    overall_passed = sum(1 for case in cases if is_passed(case))
    occlusion_passed = sum(1 for case in occlusion_cases if is_passed(case))
    overall = overall_passed / len(cases) if cases else 0.0
    occluded = occlusion_passed / len(occlusion_cases)
    return {
        "caseCount": len(occlusion_cases),
        "passRate": round(occluded, 6),
        "deltaFromOverallPassRate": round(overall - occluded, 6),
        "coverage": "covered",
    }


def negative_false_positive_rate(cases: list[dict[str, Any]]) -> dict[str, Any]:
    negative_cases: list[dict[str, Any]] = []
    false_positive_cases = 0

    for case in cases:
        metrics = case_metrics(case)
        ground_truth = as_float(metrics.get("GroundTruthCount"))
        expected_match = metrics.get("ExpectedIsMatch")
        no_match_allowed = metrics.get("NoMatchAllowed")
        is_negative = ground_truth == 0 or expected_match is False or no_match_allowed is True
        if not is_negative:
            continue

        negative_cases.append(case)
        predicted_count = as_float(metrics.get("PredictedCount"))
        actual_is_match = metrics.get("ActualIsMatch")
        false_positive_count = as_float(metrics.get("FalsePositiveCount"))
        if (predicted_count is not None and predicted_count > 0) or actual_is_match is True or (false_positive_count is not None and false_positive_count > 0):
            false_positive_cases += 1

    return {
        "negativeCaseCount": len(negative_cases),
        "falsePositiveCaseCount": false_positive_cases,
        "falsePositiveRate": pass_rate(false_positive_cases, len(negative_cases)),
    }


def build_template_matching_row(path: Path) -> dict[str, Any]:
    payload = read_json(path)
    summary = payload.get("Summary", {})
    cases = get_cases(payload)
    errors = [value for case in cases if (value := as_float(case.get("PositionErrorPx"))) is not None]
    angle_errors = [value for case in cases if (value := as_float(case.get("AngleErrorDeg"))) is not None]
    scale_errors = [value for case in cases if (value := as_float(case.get("ScaleError"))) is not None]
    pyramid_cases = [case for case in cases if (value := as_float(case.get("PyramidLevels"))) is not None and value >= 3]
    scores = [value for case in cases if (value := as_float(case.get("NormalizedScore"))) is not None]
    threshold = 0.75
    passed = int(summary.get("Passed") or 0)
    case_count = int(summary.get("CaseCount") or len(cases))
    rotation_cases = [case for case in cases if "rotation" in scenario_name(case).lower()]
    scale_cases = [case for case in cases if "scale" in scenario_name(case).lower()]

    return {
        "operator": "TemplateMatching",
        "family": "template-matching",
        "source": repo(path),
        "profile": summary.get("Profile"),
        "candidateVersion": summary.get("CandidateVersion"),
        "caseCount": case_count,
        "passed": passed,
        "failed": int(summary.get("Failed") or 0),
        "passRate": pass_rate(passed, case_count),
        "positionMetricCaseCount": len(errors),
        "meanPositionErrorPx": round_value(mean(errors)),
        "p95PositionErrorPx": round_value(percentile(errors, 0.95)),
        "maxPositionErrorPx": round_value(max(errors) if errors else None),
        "angleMetricCaseCount": len(angle_errors),
        "meanAngleErrorDeg": round_value(mean(angle_errors)),
        "p95AngleErrorDeg": round_value(percentile(angle_errors, 0.95)),
        "scaleMetricCaseCount": len(scale_errors),
        "meanScaleError": round_value(mean(scale_errors)),
        "p95ScaleError": round_value(percentile(scale_errors, 0.95)),
        "scoreThreshold": threshold,
        "minScoreMargin": round_value((min(scores) - threshold) if scores else None),
        "meanScoreMargin": round_value((mean(scores) - threshold) if scores else None),
        "falsePositive": negative_false_positive_rate(cases),
        "occlusionSensitivity": occlusion_sensitivity(cases),
        "poseReplayCoverage": {
            "rotationCaseCount": len(rotation_cases),
            "rotationPassRate": pass_rate(sum(1 for case in rotation_cases if is_passed(case)), len(rotation_cases)),
            "scaleCaseCount": len(scale_cases),
            "scalePassRate": pass_rate(sum(1 for case in scale_cases if is_passed(case)), len(scale_cases)),
            "pyramidLevelCaseCount": len(pyramid_cases),
            "maxPyramidLevels": int(max((as_float(case.get("PyramidLevels")) or 0 for case in cases), default=0)),
        },
        "scenarioSummaries": summarize_scenarios(cases),
        "notes": [
            "Homography bridge plus bounded pose-search evidence for small/medium rotation and 0.9..1.1 scale replay.",
        ],
    }


def build_shape_matching_row(path: Path) -> dict[str, Any]:
    payload = read_json(path)
    summary = payload.get("Summary", {})
    cases = get_cases(payload)
    position_errors: list[float] = []
    angle_errors: list[float] = []
    scale_errors: list[float] = []
    scores: list[float] = []

    for case in cases:
        matches = case.get("Matches")
        if not isinstance(matches, list):
            continue
        for match in matches:
            if not isinstance(match, dict) or match.get("IsTruePositive") is not True:
                continue
            if (value := as_float(match.get("PositionErrorPx"))) is not None:
                position_errors.append(value)
            if (value := as_float(match.get("AngleErrorDeg"))) is not None:
                angle_errors.append(value)
            if (value := as_float(match.get("ScaleError"))) is not None:
                scale_errors.append(value)
            if (value := as_float(match.get("Score"))) is not None:
                scores.append(value)

    threshold = 0.40
    passed = int(summary.get("Passed") or 0)
    case_count = int(summary.get("CaseCount") or len(cases))
    rotation_cases = [case for case in cases if "rotated" in scenario_name(case).lower() or "rotation" in scenario_name(case).lower()]
    scale_cases = [case for case in cases if "scaled" in scenario_name(case).lower() or "scale" in scenario_name(case).lower()]

    return {
        "operator": "ShapeMatching",
        "family": "shape-family",
        "source": repo(path),
        "profile": summary.get("Profile"),
        "candidateVersion": summary.get("CandidateVersion"),
        "caseCount": case_count,
        "passed": passed,
        "failed": int(summary.get("Failed") or 0),
        "passRate": pass_rate(passed, case_count),
        "positionMetricCaseCount": len(position_errors),
        "meanPositionErrorPx": round_value(mean(position_errors)),
        "p95PositionErrorPx": round_value(percentile(position_errors, 0.95)),
        "maxPositionErrorPx": round_value(max(position_errors) if position_errors else None),
        "angleMetricCaseCount": len(angle_errors),
        "meanAngleErrorDeg": round_value(mean(angle_errors)),
        "p95AngleErrorDeg": round_value(percentile(angle_errors, 0.95)),
        "scaleMetricCaseCount": len(scale_errors),
        "meanScaleError": round_value(mean(scale_errors)),
        "p95ScaleError": round_value(percentile(scale_errors, 0.95)),
        "precision": round_value(as_float(summary.get("Precision"))),
        "recall": round_value(as_float(summary.get("Recall"))),
        "f1": round_value(as_float(summary.get("F1"))),
        "scoreThreshold": threshold,
        "minScoreMargin": round_value((min(scores) - threshold) if scores else None),
        "meanScoreMargin": round_value((mean(scores) - threshold) if scores else None),
        "falsePositive": negative_false_positive_rate(cases),
        "occlusionSensitivity": occlusion_sensitivity(cases),
        "poseReplayCoverage": {
            "rotationCaseCount": len(rotation_cases),
            "rotationPassRate": pass_rate(sum(1 for case in rotation_cases if is_passed(case)), len(rotation_cases)),
            "scaleCaseCount": len(scale_cases),
            "scalePassRate": pass_rate(sum(1 for case in scale_cases if is_passed(case)), len(scale_cases)),
        },
        "scenarioSummaries": summarize_scenarios(cases),
        "notes": [
            "Primary pose-search profile for rotation, scale, multi-target, top-left origin, and blank-negative replay.",
        ],
    }


def build_contract_baseline_row(path: Path, operator: str, score_field: str, score_threshold: float) -> dict[str, Any]:
    payload = read_json(path)
    summary = payload.get("Summary", {})
    cases = get_cases(payload)
    position_errors: list[float] = []
    angle_errors: list[float] = []
    scale_errors: list[float] = []
    scores: list[float] = []

    for case in cases:
        metrics = case_metrics(case)
        if (value := first_float(metrics, "PositionErrorPx", "MeanPositionErrorPx")) is not None:
            position_errors.append(value)
        if (value := first_float(metrics, "AngleErrorDeg", "MeanAngleErrorDeg")) is not None:
            angle_errors.append(value)
        if (value := first_float(metrics, "ScaleError", "MeanScaleError")) is not None:
            scale_errors.append(value)
        is_positive_case = (
            metrics.get("ExpectedIsMatch") is not False
            and metrics.get("NoMatchAllowed") is not True
            and as_float(metrics.get("GroundTruthCount")) != 0
        )
        if is_positive_case and (value := as_float(metrics.get(score_field))) is not None and value > 0:
            scores.append(value)

    passed = int(summary.get("Passed") or 0)
    case_count = int(summary.get("CaseCount") or len(cases))
    rotation_cases = [case for case in cases if "rotation" in scenario_name(case).lower()]
    scale_cases = [case for case in cases if "scale" in scenario_name(case).lower() or "scaled" in scenario_name(case).lower()]

    return {
        "operator": operator,
        "family": "shape-family",
        "source": repo(path),
        "profile": "contract_baseline",
        "candidateVersion": "control",
        "caseCount": case_count,
        "passed": passed,
        "failed": int(summary.get("Failed") or 0),
        "passRate": pass_rate(passed, case_count),
        "positionMetricCaseCount": len(position_errors),
        "meanPositionErrorPx": round_value(mean(position_errors)),
        "p95PositionErrorPx": round_value(percentile(position_errors, 0.95)),
        "maxPositionErrorPx": round_value(max(position_errors) if position_errors else None),
        "angleMetricCaseCount": len(angle_errors),
        "meanAngleErrorDeg": round_value(mean(angle_errors)),
        "p95AngleErrorDeg": round_value(percentile(angle_errors, 0.95)),
        "scaleMetricCaseCount": len(scale_errors),
        "meanScaleError": round_value(mean(scale_errors)),
        "p95ScaleError": round_value(percentile(scale_errors, 0.95)),
        "scoreThreshold": score_threshold,
        "minScoreMargin": round_value((min(scores) - score_threshold) if scores else None),
        "meanScoreMargin": round_value((mean(scores) - score_threshold) if scores else None),
        "falsePositive": negative_false_positive_rate(cases),
        "occlusionSensitivity": occlusion_sensitivity(cases),
        "poseReplayCoverage": {
            "rotationCaseCount": len(rotation_cases),
            "rotationPassRate": pass_rate(sum(1 for case in rotation_cases if is_passed(case)), len(rotation_cases)),
            "scaleCaseCount": len(scale_cases),
            "scalePassRate": pass_rate(sum(1 for case in scale_cases if is_passed(case)), len(scale_cases)),
        },
        "scenarioSummaries": summarize_scenarios(cases),
        "notes": [
            "Contract baseline included for family comparison; missing angle/scale fields are reported as null rather than inferred.",
        ],
    }


def load_operator_rows() -> tuple[list[dict[str, Any]], list[str]]:
    rows: list[dict[str, Any]] = []
    errors: list[str] = []
    builders = [
        (TEMPLATE_JSON, lambda path: build_template_matching_row(path)),
        (SHAPE_JSON, lambda path: build_shape_matching_row(path)),
        (GRADIENT_JSON, lambda path: build_contract_baseline_row(path, "GradientShapeMatch", "ScoreValue", 80.0)),
        (PYRAMID_JSON, lambda path: build_contract_baseline_row(path, "PyramidShapeMatch", "Score", 80.0)),
    ]

    for path, builder in builders:
        if not path.exists():
            errors.append(f"missing source report: {repo(path)}")
            continue
        rows.append(builder(path))

    return rows, errors


def validate_rows(rows: list[dict[str, Any]], source_errors: list[str]) -> list[str]:
    errors = list(source_errors)
    by_operator = {str(row.get("operator")): row for row in rows}

    for operator in ("TemplateMatching", "ShapeMatching", "GradientShapeMatch", "PyramidShapeMatch"):
        if operator not in by_operator:
            errors.append(f"{operator} missing from precision leaderboard")

    template = by_operator.get("TemplateMatching")
    if template is not None:
        if int(template.get("caseCount") or 0) < 20:
            errors.append("TemplateMatching v2 must execute at least 20 homography bridge cases")
        if int(template.get("failed") or 0) != 0:
            errors.append("TemplateMatching v2 must not have failed bridge cases")
        p95 = as_float(template.get("p95PositionErrorPx"))
        if p95 is None or p95 > 1.5:
            errors.append("TemplateMatching v2 P95 position error must be <= 1.5 px")
        coverage = template.get("poseReplayCoverage", {})
        if int(coverage.get("rotationCaseCount") or 0) == 0:
            errors.append("TemplateMatching v2 must include rotation replay cases")
        if int(coverage.get("scaleCaseCount") or 0) == 0:
            errors.append("TemplateMatching v2 must include scale replay cases")
        if int(template.get("angleMetricCaseCount") or 0) == 0 or int(template.get("scaleMetricCaseCount") or 0) == 0:
            errors.append("TemplateMatching v2 must expose angle and scale error metrics")
        if int(coverage.get("pyramidLevelCaseCount") or 0) == 0 or int(coverage.get("maxPyramidLevels") or 0) < 3:
            errors.append("TemplateMatching v2 must include at least one 3-level pyramid pose-search replay case")

    shape = by_operator.get("ShapeMatching")
    if shape is not None:
        if int(shape.get("caseCount") or 0) < 30:
            errors.append("ShapeMatching v2 must execute the full geometric pose dataset, including negatives")
        if int(shape.get("failed") or 0) != 0:
            errors.append("ShapeMatching v2 must not have failed pose cases")
        p95 = as_float(shape.get("p95PositionErrorPx"))
        if p95 is None or p95 > 8.0:
            errors.append("ShapeMatching v2 P95 position error must be <= 8 px")
        if shape.get("falsePositive", {}).get("falsePositiveCaseCount") != 0:
            errors.append("ShapeMatching v2 blank-negative false positive count must be 0")
        if int(shape.get("angleMetricCaseCount") or 0) == 0 or int(shape.get("scaleMetricCaseCount") or 0) == 0:
            errors.append("ShapeMatching v2 must expose angle and scale error metrics")

    for operator in ("GradientShapeMatch", "PyramidShapeMatch"):
        row = by_operator.get(operator)
        if row is not None and int(row.get("failed") or 0) != 0:
            errors.append(f"{operator} contract baseline must not have failed cases")

    return errors


def build_report() -> dict[str, Any]:
    rows, source_errors = load_operator_rows()
    errors = validate_rows(rows, source_errors)
    total_cases = sum(int(row.get("caseCount") or 0) for row in rows)
    total_passed = sum(int(row.get("passed") or 0) for row in rows)
    total_failed = sum(int(row.get("failed") or 0) for row in rows)

    return {
        "schemaVersion": "2026-04-30.shape-matching-precision.v2",
        "evidenceKind": "shape-matching-precision",
        "generatedAtUtc": utc_now(),
        "accepted": len(errors) == 0,
        "summary": {
            "operatorCount": len(rows),
            "totalCaseCount": total_cases,
            "totalPassed": total_passed,
            "totalFailed": total_failed,
            "overallPassRate": pass_rate(total_passed, total_cases),
            "primaryProfile": {
                "operator": "ShapeMatching",
                "profile": "geometric_dataset_precision_v2",
                "reason": "Best current source for pose labels with position, angle, scale, multi-target, origin, and blank-negative checks.",
            },
            "conservativeFallbackProfile": {
                "operator": "GradientShapeMatch",
                "profile": "contract_baseline",
                "reason": "Best current fallback evidence for rotation and occlusion-style shape scenes while ShapeMatching remains the scale-aware precision profile.",
            },
            "claimBoundary": "This is reproducible public-protocol and semi-synthetic precision evidence, not real production field sign-off.",
        },
        "gates": {
            "accepted": len(errors) == 0,
            "errors": errors,
        },
        "operatorLeaderboard": rows,
    }


def render_markdown(report: dict[str, Any]) -> str:
    summary = report["summary"]
    lines = [
        "# Shape Matching Precision v2",
        "",
        f"GeneratedAtUtc: `{report['generatedAtUtc']}`",
        f"Accepted: `{report['accepted']}`",
        f"ClaimBoundary: `{summary['claimBoundary']}`",
        "",
        "## Summary",
        "",
        "| Metric | Value |",
        "| --- | ---: |",
        f"| Operators | {summary['operatorCount']} |",
        f"| Total cases | {summary['totalCaseCount']} |",
        f"| Passed | {summary['totalPassed']} |",
        f"| Failed | {summary['totalFailed']} |",
        f"| Overall pass rate | {summary['overallPassRate']} |",
        "",
        "## Leaderboard",
        "",
        "| Operator | Cases | Passed | Failed | Pos P95 px | Angle mean deg | Scale mean | Min score margin | Neg FP rate | Occlusion cases | Source |",
        "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |",
    ]

    for row in report["operatorLeaderboard"]:
        false_positive = row.get("falsePositive", {})
        occlusion = row.get("occlusionSensitivity", {})
        lines.append(
            f"| {row['operator']} | {row['caseCount']} | {row['passed']} | {row['failed']} | "
            f"{format_metric(row.get('p95PositionErrorPx'))} | {format_metric(row.get('meanAngleErrorDeg'))} | "
            f"{format_metric(row.get('meanScaleError'))} | {format_metric(row.get('minScoreMargin'))} | "
            f"{format_metric(false_positive.get('falsePositiveRate'))} | {occlusion.get('caseCount', 0)} | {row['source']} |"
        )

    lines.extend(
        [
            "",
            "## Pose Coverage",
            "",
            "| Operator | Rotation cases | Rotation pass rate | Scale cases | Scale pass rate | Pyramid >=3 cases | Max pyramid levels | Position metric cases | Angle metric cases | Scale metric cases |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |",
        ]
    )

    for row in report["operatorLeaderboard"]:
        coverage = row.get("poseReplayCoverage", {})
        lines.append(
            f"| {row['operator']} | {coverage.get('rotationCaseCount', 0)} | {format_metric(coverage.get('rotationPassRate'))} | "
            f"{coverage.get('scaleCaseCount', 0)} | {format_metric(coverage.get('scalePassRate'))} | "
            f"{coverage.get('pyramidLevelCaseCount', 0)} | {coverage.get('maxPyramidLevels', 0)} | "
            f"{row.get('positionMetricCaseCount', 0)} | {row.get('angleMetricCaseCount', 0)} | {row.get('scaleMetricCaseCount', 0)} |"
        )

    lines.extend(
        [
            "",
            "## Profile Decision",
            "",
            f"- Primary: `{summary['primaryProfile']['operator']}` / `{summary['primaryProfile']['profile']}` - {summary['primaryProfile']['reason']}",
            f"- Fallback: `{summary['conservativeFallbackProfile']['operator']}` / `{summary['conservativeFallbackProfile']['profile']}` - {summary['conservativeFallbackProfile']['reason']}",
            "- TemplateMatching now includes bounded pose-search replay for small/medium rotation and 0.9..1.1 scale, with angle/scale error metrics reported from the v2 bridge.",
            "",
            "## Gates",
            "",
        ]
    )

    gate_errors = report["gates"]["errors"]
    if gate_errors:
        lines.extend(f"- ERROR: {error}" for error in gate_errors)
    else:
        lines.append("- All gates passed.")

    lines.append("")
    return "\n".join(lines)


def format_metric(value: Any) -> str:
    if value is None:
        return "-"
    if isinstance(value, float):
        return f"{value:.6g}"
    return str(value)


def run_command(command: list[str]) -> None:
    print("[shape-precision]", " ".join(command), flush=True)
    completed = subprocess.run(command, cwd=REPO_ROOT)
    if completed.returncode != 0:
        raise SystemExit(completed.returncode)


def execute_template_matching() -> None:
    run_command(
        [
            "dotnet",
            "run",
            "--project",
            TEMPLATE_PROJECT,
            "--",
            "--output",
            repo(TEMPLATE_JSON),
            "--report",
            repo(TEMPLATE_MD),
            "--candidate-version",
            "v2",
            "--profile",
            "homography_bridge_precision_v2",
        ]
    )


def execute_shape_matching() -> None:
    run_command(
        [
            "dotnet",
            "run",
            "--project",
            SHAPE_PROJECT,
            "--",
            "--output",
            repo(SHAPE_JSON),
            "--report",
            repo(SHAPE_MD),
            "--candidate-version",
            "v2",
            "--profile",
            "geometric_dataset_precision_v2",
        ]
    )


def validate_existing_report(path: Path) -> list[str]:
    if not path.exists():
        return [f"missing precision report: {repo(path)}"]
    report = read_json(path)
    errors = list(report.get("gates", {}).get("errors") or [])
    if report.get("accepted") is not True:
        errors.append("precision report accepted flag is not true")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description="Build Template/Shape matching precision v2 evidence.")
    parser.add_argument("--execute-candidates", action="store_true", help="Run TemplateMatching and ShapeMatching candidate runners before building the report.")
    parser.add_argument("--execute-template-matching", action="store_true", help="Run only the TemplateMatching homography bridge candidate.")
    parser.add_argument("--execute-shape-matching", action="store_true", help="Run only the ShapeMatching geometric dataset candidate.")
    parser.add_argument("--validate-only", action="store_true", help="Validate the existing precision v2 report.")
    parser.add_argument("--output", default=str(OUTPUT_JSON), help="Combined JSON output path.")
    parser.add_argument("--report", default=str(OUTPUT_MD), help="Combined Markdown output path.")
    args = parser.parse_args()

    output = Path(args.output)
    if not output.is_absolute():
        output = REPO_ROOT / output
    markdown = Path(args.report)
    if not markdown.is_absolute():
        markdown = REPO_ROOT / markdown

    if args.validate_only:
        errors = validate_existing_report(output)
        if errors:
            for error in errors:
                print(f"error: {error}", file=sys.stderr)
            return 2
        print(f"validated {repo(output)}")
        return 0

    if args.execute_candidates or args.execute_template_matching:
        execute_template_matching()
    if args.execute_candidates or args.execute_shape_matching:
        execute_shape_matching()

    report = build_report()
    write_json(output, report)
    write_text(markdown, render_markdown(report))

    print(
        f"shape matching precision v2 complete: accepted={report['accepted']}, "
        f"cases={report['summary']['totalCaseCount']}, failed={report['summary']['totalFailed']}, output={repo(output)}"
    )
    return 0 if report["accepted"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
