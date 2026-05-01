from __future__ import annotations

import argparse
import hashlib
import json
import os
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from build_core20_proof_assets import (
    CORE20_OPERATORS,
    REPORT_DIR,
    SECONDARY_METRICS,
    field_algorithm_proof_baseline_path,
    field_algorithm_proof_report_path,
    field_manifest_path,
    freeze_thresholds,
    is_lower_better,
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
from ingest_core20_field_dataset import (
    IngestError,
    case_label,
    ensure_no_sensitive_json,
    field_root,
    find_case_list,
    is_failure_or_boundary,
    load_cases,
    resolve_dataset_root,
    taxonomy_values,
    validate_case_document,
    validate_operator_case,
)


PILOT_OPERATORS = ("SurfaceDefectDetection", "DeepLearning", "CaliperTool")
RESULT_CANDIDATES = ("proof_results.json", "algorithm_results.json", "test_results.json")
SUM_METRICS = {
    "DetectionCount",
    "FalseNegativeCount",
    "FalsePositiveCount",
    "GroundTruthCount",
    "TruePositiveCount",
}


class ProofError(Exception):
    pass


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def operator_item(operator: str) -> dict[str, Any]:
    for item in G3_OPERATORS:
        if item["operator"] == operator:
            return item
    raise ProofError(f"Unknown Core20 operator: {operator}")


def normalize_bool(value: Any) -> bool | None:
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        text = value.strip().lower()
        if text in {"true", "pass", "passed", "success", "ok", "accepted"}:
            return True
        if text in {"false", "fail", "failed", "error", "rejected"}:
            return False
    return None


def result_case_id(result: dict[str, Any]) -> str:
    value = result.get("caseId", result.get("CaseId"))
    return str(value or "").strip()


def result_metrics(result: dict[str, Any]) -> dict[str, float]:
    raw = result.get("metrics", result.get("Metrics", {}))
    metrics: dict[str, float] = {}
    if isinstance(raw, dict):
        for key, value in raw.items():
            if isinstance(value, (int, float)) and not isinstance(value, bool):
                metrics[str(key)] = float(value)
    for key, value in result.items():
        if key in {"caseId", "CaseId", "passed", "Passed", "metrics", "Metrics", "prediction", "failureTaxonomy"}:
            continue
        if isinstance(value, (int, float)) and not isinstance(value, bool):
            metrics.setdefault(str(key), float(value))
    return metrics


def predicted_label(result: dict[str, Any]) -> str:
    value = result.get("predictedLabel") or result.get("PredictedLabel")
    prediction = result.get("prediction") or result.get("Prediction")
    if value is None and isinstance(prediction, dict):
        value = prediction.get("label") or prediction.get("imageLabel") or prediction.get("className")
    return str(value or "").strip().lower()


def positive_label(value: str) -> bool:
    return value in {"ng", "defect", "anomaly", "positive", "match", "found"}


def infer_passed(operator: str, case: dict[str, Any], result: dict[str, Any], thresholds: dict[str, Any]) -> bool | None:
    explicit = normalize_bool(result.get("passed", result.get("Passed", result.get("success", result.get("accepted")))))
    if explicit is not None:
        return explicit

    metrics = result_metrics(result)
    if operator == "CaliperTool" and "WidthErrorPx" in metrics:
        threshold = thresholds.get("WidthErrorPx")
        if isinstance(threshold, (int, float)):
            count_ok = metrics.get("PairCountAccuracy", 1.0) >= 1.0
            return metrics["WidthErrorPx"] <= float(threshold) and count_ok

    if operator == "DeepLearning" and {"FalsePositiveCount", "FalseNegativeCount"}.issubset(metrics):
        return metrics["FalsePositiveCount"] == 0 and metrics["FalseNegativeCount"] == 0

    label = case_label(case)
    predicted = predicted_label(result)
    if predicted:
        return positive_label(label) == positive_label(predicted)
    return None


def find_result_file(root: Path, explicit: str | None) -> Path | None:
    if explicit:
        path = Path(explicit)
        if not path.is_absolute():
            path = root / path
        return path.resolve()
    for name in RESULT_CANDIDATES:
        candidate = root / name
        if candidate.exists():
            return candidate.resolve()
    return None


def load_result_document(path: Path, operator: str) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    with path.open("r", encoding="utf-8-sig") as handle:
        document = json.load(handle)
    if not isinstance(document, dict):
        raise ProofError(f"{path.name} root must be an object")
    if document.get("operator") and document.get("operator") != operator:
        raise ProofError(f"{path.name} operator mismatch: {document.get('operator')} != {operator}")
    results = document.get("results") or document.get("perCaseResults") or document.get("cases")
    if not isinstance(results, list):
        raise ProofError(f"{path.name} must contain results, perCaseResults, or cases list")
    for index, result in enumerate(results):
        if not isinstance(result, dict):
            raise ProofError(f"{path.name}.results[{index}] must be an object")
    return results, document


def aggregate_metrics(rows: list[dict[str, Any]], document: dict[str, Any]) -> dict[str, Any]:
    aggregate: dict[str, Any] = {}
    top_level = document.get("metrics") or document.get("Metrics") or {}
    if isinstance(top_level, dict):
        for key, value in top_level.items():
            if isinstance(value, (int, float)) and not isinstance(value, bool):
                aggregate[str(key)] = round(float(value), 6)

    case_count = len(rows)
    passed = sum(1 for row in rows if row["passed"] is True)
    failed = case_count - passed
    aggregate["CaseCount"] = case_count
    aggregate["Passed"] = passed
    aggregate["Failed"] = failed
    aggregate["PassRate"] = round(passed / case_count, 6) if case_count else 0.0

    runtime_values = [
        float(row["runtimeMs"])
        for row in rows
        if isinstance(row.get("runtimeMs"), (int, float)) and not isinstance(row.get("runtimeMs"), bool)
    ]
    if runtime_values:
        values = sorted(runtime_values)
        p95_index = min(len(values) - 1, int(round((len(values) - 1) * 0.95)))
        aggregate.setdefault("RuntimeMsAvg", round(sum(values) / len(values), 6))
        aggregate.setdefault("RuntimeMsP95", round(values[p95_index], 6))

    metric_values: dict[str, list[float]] = {}
    for row in rows:
        for key, value in row.get("metrics", {}).items():
            if isinstance(value, (int, float)) and not isinstance(value, bool):
                metric_values.setdefault(str(key), []).append(float(value))

    for key, values in metric_values.items():
        if key in aggregate or not values:
            continue
        if key in SUM_METRICS:
            aggregate[key] = round(sum(values), 6)
        elif is_lower_better(key):
            aggregate[key] = round(max(values), 6)
        else:
            aggregate[key] = round(sum(values) / len(values), 6)

    tp = aggregate.get("TruePositiveCount")
    fp = aggregate.get("FalsePositiveCount")
    fn = aggregate.get("FalseNegativeCount")
    if isinstance(tp, (int, float)) and isinstance(fp, (int, float)):
        aggregate.setdefault("PrecisionAt50", round(tp / (tp + fp), 6) if tp + fp else 1.0)
    if isinstance(tp, (int, float)) and isinstance(fn, (int, float)):
        aggregate.setdefault("RecallAt50", round(tp / (tp + fn), 6) if tp + fn else 1.0)

    return aggregate


def threshold_gate(metric: str, value: Any, threshold: Any) -> dict[str, Any]:
    if not isinstance(threshold, (int, float)) or isinstance(threshold, bool):
        return {"metric": metric, "threshold": threshold, "value": value, "passed": True, "ignored": True}
    if not isinstance(value, (int, float)) or isinstance(value, bool):
        return {"metric": metric, "threshold": threshold, "value": value, "passed": False, "missing": True}
    if metric == "Failed" or is_lower_better(metric):
        passed = float(value) <= float(threshold)
        comparator = "<="
    else:
        passed = float(value) >= float(threshold)
        comparator = ">="
    return {
        "metric": metric,
        "threshold": threshold,
        "value": round(float(value), 6),
        "comparator": comparator,
        "passed": passed,
    }


def validate_repo_config(operators: list[str]) -> None:
    missing = []
    for operator in operators:
        operator_item(operator)
        if operator not in PILOT_OPERATORS:
            raise ProofError(f"{operator} is not enabled for pilot algorithm proof yet")
        for path in (field_manifest_path(operator), split_path(operator)):
            if not path.exists():
                missing.append(repo(path))
    if missing:
        raise ProofError("missing generated Core20 proof assets:\n" + "\n".join(missing))


def build_operator_row(
    operator: str,
    item: dict[str, Any],
    manifest_sha256: str,
    split: dict[str, Any],
    aggregate_metrics: dict[str, Any],
    per_case_rows: list[dict[str, Any]],
    threshold_results: list[dict[str, Any]],
    accepted: bool,
    result_file: Path,
    privacy_leak_count: int,
    raw_path_leak_count: int,
) -> dict[str, Any]:
    status = "field-algorithm-proof-passed" if accepted else "field-algorithm-proof-failed"
    return {
        "Operator": operator,
        "datasetId": proof_name(operator),
        "manifest": repo(field_manifest_path(operator)),
        "manifestSha256": manifest_sha256,
        "splitSummary": {
            "strategy": split["strategy"],
            "seed": split["seed"],
            "trainCount": split["counts"]["train"],
            "validationCount": split["counts"]["validation"],
            "testCount": split["counts"]["test"],
            "evaluatedSplit": "test",
            "evaluatedCaseCount": len(per_case_rows),
            "noOverlap": True,
            "caseListPath": repo(split_path(operator)),
        },
        "algorithmResultSource": {
            "rootEnv": "CLEARVISION_PRODUCTION_DATASET_ROOT",
            "relativeRoot": f"core20/{operator}/field_v1",
            "file": result_file.name,
            "checksumSha256": sha256_file(result_file),
        },
        "metrics": {
            "primary": item["primaryMetric"],
            "secondary": SECONDARY_METRICS[operator],
            "values": aggregate_metrics,
            "thresholds": freeze_thresholds(item),
            "thresholdResults": threshold_results,
        },
        "thresholds": freeze_thresholds(item),
        "perCaseResults": per_case_rows,
        "failureTaxonomy": item["boundaries"],
        "privacyLeakCount": privacy_leak_count,
        "rawPathLeakCount": raw_path_leak_count,
        "accepted": accepted,
        "proofLevel": "field-algorithm-proof",
        "proofStatus": status,
        "industrialStatus": (
            "algorithm proof passed on frozen test split; real site/line sign-off is still pending"
            if accepted
            else "algorithm proof failed or incomplete on frozen test split; real industrial validation is not complete"
        ),
    }


def render_report(baseline: dict[str, Any]) -> str:
    summary = baseline["Summary"]
    row = baseline["Operators"][0]
    lines = [
        f"# {summary['Operator']} Field Algorithm Proof",
        "",
        f"GeneratedAtUtc: `{summary['GeneratedAtUtc']}`",
        "",
        "## Summary",
        "",
        f"- DatasetId: `{summary['DatasetId']}`",
        f"- Evaluated split: `test`",
        f"- Test cases: {summary['TestCaseCount']}",
        f"- Passed/failed cases: {summary['Passed']}/{summary['Failed']}",
        f"- Privacy/raw-path leaks: {summary['PrivacyLeakCount']}/{summary['RawPathLeakCount']}",
        f"- Threshold gate passed: {'Yes' if summary['ThresholdGatePassed'] else 'No'}",
        f"- Accepted proof: {'Yes' if summary['Accepted'] else 'No'}",
        f"- Proof status: `{summary['ProofStatus']}`",
        "",
        "## Metrics",
        "",
        "| Metric | Value | Threshold | Gate |",
        "|---|---:|---:|---|",
    ]
    values = row["metrics"]["values"]
    thresholds = row["metrics"]["thresholds"]
    for gate in row["metrics"]["thresholdResults"]:
        metric = gate["metric"]
        lines.append(
            f"| {metric} | {values.get(metric, 'missing')} | {thresholds.get(metric, '')} | "
            f"{'PASS' if gate.get('passed') else 'FAIL'} |"
        )
    lines.extend(
        [
            "",
            "## Proof Contract",
            "",
            "- The runner evaluates only case ids in the frozen `test` split.",
            "- The external algorithm result file must contain one result per test case and no raw customer path.",
            "- This proof is algorithm/test-split evidence; final industrial validation still requires site/line sign-off.",
            "",
        ]
    )
    return "\n".join(lines)


def update_aggregate_baseline(operator: str, operator_row: dict[str, Any]) -> None:
    path = REPORT_DIR / "QualityFlywheel_core20_proof_baseline.json"
    aggregate = read_json(path)
    rows = aggregate.get("Operators", [])
    for index, row in enumerate(rows):
        if row.get("Operator") == operator:
            rows[index] = operator_row
            break
    else:
        rows.append(operator_row)

    summary = aggregate.setdefault("Summary", {})
    summary["Accepted"] = sum(1 for row in rows if row.get("accepted") is True)
    summary["AlgorithmProofPassed"] = sum(1 for row in rows if row.get("proofStatus") == "field-algorithm-proof-passed")
    summary["AlgorithmProofFailed"] = sum(1 for row in rows if row.get("proofStatus") == "field-algorithm-proof-failed")
    summary["BlockedMissingFieldData"] = sum(1 for row in rows if row.get("proofStatus") == "blocked-missing-field-data")
    summary["FieldDataReady"] = sum(1 for row in rows if row.get("proofStatus") == "field-data-ready")
    summary["ProofGatePassed"] = summary["Accepted"] == len(CORE20_OPERATORS)
    summary["ProofGateInterpretation"] = (
        "All Core20 operators have accepted algorithm proof on frozen test splits."
        if summary["ProofGatePassed"]
        else "Some Core20 operators are still blocked, data-ready only, or algorithm-proof failed."
    )
    aggregate["Operators"] = rows
    write_json(path, aggregate)
    (REPORT_DIR / "QualityFlywheel_core20_proof_baseline.md").write_text(
        render_proof_report(aggregate), encoding="utf-8", newline="\n"
    )


def update_registry(operator: str, operator_row: dict[str, Any]) -> None:
    path = REPORT_DIR / "QualityFlywheel_core20_proof_registry.json"
    registry = read_json(path)
    for row in registry.get("operators", []):
        if row.get("operator") == operator:
            row["proofStatus"] = operator_row["proofStatus"]
            row["proofLevel"] = operator_row["proofLevel"]
            row["algorithmProofBaselineJson"] = repo(field_algorithm_proof_baseline_path(operator))
            row["algorithmProofReportMarkdown"] = repo(field_algorithm_proof_report_path(operator))
            row["industrialStatus"] = operator_row["industrialStatus"]
            break
    summary = registry.setdefault("summary", {})
    summary["algorithmProofPassedCount"] = sum(
        1 for row in registry.get("operators", []) if row.get("proofStatus") == "field-algorithm-proof-passed"
    )
    summary["algorithmProofFailedCount"] = sum(
        1 for row in registry.get("operators", []) if row.get("proofStatus") == "field-algorithm-proof-failed"
    )
    summary["realIndustrialValidationComplete"] = 0
    write_json(path, registry)
    (REPORT_DIR / "QualityFlywheel_core20_proof_registry.md").write_text(
        render_registry_markdown(registry), encoding="utf-8", newline="\n"
    )


def run_operator(operator: str, dataset_root: Path | None, result_file_arg: str | None, dry_run: bool, allow_missing_data: bool) -> int:
    item = operator_item(operator)
    validate_repo_config([operator])
    if dataset_root is None:
        if allow_missing_data:
            print(f"[{operator}] CLEARVISION_PRODUCTION_DATASET_ROOT is not set; algorithm proof remains blocked.")
            return 0
        raise ProofError("CLEARVISION_PRODUCTION_DATASET_ROOT is not set; pass --dataset-root or --allow-missing-data")

    root = field_root(dataset_root, operator)
    if not root.exists():
        if allow_missing_data:
            print(f"[{operator}] field root missing: {root}; algorithm proof remains blocked.")
            return 0
        raise ProofError(f"{operator} field root does not exist: {root}")

    case_list = find_case_list(root)
    if case_list is None:
        if allow_missing_data:
            print(f"[{operator}] no case list under {root}; algorithm proof remains blocked.")
            return 0
        raise ProofError(f"{operator} case list not found; expected cases.json, cases.jsonl, or case_manifest.json")

    result_file = find_result_file(root, result_file_arg)
    if result_file is None or not result_file.exists():
        if allow_missing_data:
            print(f"[{operator}] algorithm proof results missing under {root}; expected one of {', '.join(RESULT_CANDIDATES)}.")
            return 0
        raise ProofError(f"{operator} algorithm proof result file not found under {root}")

    cases, case_document = load_cases(case_list, operator)
    errors: list[str] = []
    validate_case_document(case_document, operator, errors)
    case_by_id: dict[str, dict[str, Any]] = {}
    for index, case in enumerate(cases):
        if not isinstance(case, dict):
            errors.append(f"case[{index}] must be an object")
            continue
        case_id = str(case.get("caseId") or "")
        case_by_id[case_id] = case
        validate_operator_case(operator, root, case, index, errors)

    split = read_json(split_path(operator))
    test_ids = [str(value) for value in split.get("test", [])]
    if split.get("operator") != operator:
        errors.append(f"{repo(split_path(operator))} operator mismatch")
    if not test_ids:
        errors.append(f"{operator} split has no test cases; run field ingest first")

    raw_results, result_document = load_result_document(result_file, operator)
    privacy_errors: list[str] = []
    ensure_no_sensitive_json(result_document, "proofResults", privacy_errors)
    raw_path_leak_count = sum(1 for error in privacy_errors if "raw path" in error.lower())

    by_case: dict[str, dict[str, Any]] = {}
    duplicates: set[str] = set()
    for result in raw_results:
        case_id = result_case_id(result)
        if not case_id:
            errors.append("algorithm proof result missing caseId")
            continue
        if case_id in by_case:
            duplicates.add(case_id)
        by_case[case_id] = result
    for case_id in sorted(duplicates):
        errors.append(f"duplicate algorithm proof result caseId: {case_id}")

    test_set = set(test_ids)
    missing_results = [case_id for case_id in test_ids if case_id not in by_case]
    missing_cases = [case_id for case_id in test_ids if case_id not in case_by_id]
    if missing_results:
        errors.append(f"algorithm proof results missing {len(missing_results)} test case ids")
    if missing_cases:
        errors.append(f"case manifest missing {len(missing_cases)} test case ids")

    thresholds = freeze_thresholds(item)
    per_case_rows: list[dict[str, Any]] = []
    taxonomy_hits = 0
    taxonomy_required = 0
    for case_id in test_ids:
        if case_id not in by_case or case_id not in case_by_id:
            continue
        case = case_by_id[case_id]
        result = by_case[case_id]
        metrics = result_metrics(result)
        passed = infer_passed(operator, case, result, thresholds)
        if passed is None:
            errors.append(f"{case_id} result must include passed/success/accepted or enough metrics to infer pass/fail")
            passed = False
        runtime_ms = result.get("runtimeMs", result.get("RuntimeMs"))
        expected_taxonomy = taxonomy_values(case)
        result_taxonomy = result.get("failureTaxonomy") or result.get("taxonomy") or []
        if isinstance(result_taxonomy, str):
            result_taxonomy = [result_taxonomy]
        if not isinstance(result_taxonomy, list):
            result_taxonomy = []
        taxonomy_hit = bool(expected_taxonomy) and bool(
            set(map(str, expected_taxonomy)) & set(map(str, result_taxonomy))
        )
        if is_failure_or_boundary(case):
            taxonomy_required += 1
            if taxonomy_hit:
                taxonomy_hits += 1
        per_case_rows.append(
            {
                "caseId": case_id,
                "split": "test",
                "label": case_label(case),
                "expectedFailureTaxonomy": expected_taxonomy,
                "reportedFailureTaxonomy": [str(value) for value in result_taxonomy],
                "failureTaxonomyHit": taxonomy_hit,
                "passed": bool(passed),
                "predictedLabel": predicted_label(result),
                "metrics": metrics,
                "runtimeMs": runtime_ms if isinstance(runtime_ms, (int, float)) and not isinstance(runtime_ms, bool) else None,
                "errorMessage": result.get("errorMessage", result.get("ErrorMessage")),
            }
        )

    aggregate = aggregate_metrics(per_case_rows, result_document)
    aggregate["MissingResultCount"] = len(missing_results)
    aggregate["FailureTaxonomyCoverage"] = round(taxonomy_hits / taxonomy_required, 6) if taxonomy_required else 1.0
    threshold_results = [
        threshold_gate(metric, aggregate.get(metric), value)
        for metric, value in thresholds.items()
        if not metric.startswith("LegacyCurrent") and metric != "thresholdFreezePolicy"
    ]
    primary = item["primaryMetric"]
    if primary not in aggregate:
        errors.append(f"{operator} primary metric missing from proof results: {primary}")
    threshold_gate_passed = all(result.get("passed") for result in threshold_results)
    accepted = (
        not errors
        and not privacy_errors
        and len(missing_results) == 0
        and len(missing_cases) == 0
        and aggregate["Failed"] == 0
        and threshold_gate_passed
        and aggregate["FailureTaxonomyCoverage"] >= 1.0
    )

    manifest_sha = sha256_file(field_manifest_path(operator))
    operator_row = build_operator_row(
        operator,
        item,
        manifest_sha,
        split,
        aggregate,
        per_case_rows,
        threshold_results,
        accepted,
        result_file,
        len(privacy_errors),
        raw_path_leak_count,
    )
    baseline = {
        "EvidenceKind": "field-algorithm-proof",
        "Summary": {
            "GeneratedAtUtc": utc_now(),
            "Operator": operator,
            "DatasetId": proof_name(operator),
            "TestCaseCount": len(per_case_rows),
            "Passed": aggregate["Passed"],
            "Failed": aggregate["Failed"],
            "MissingResultCount": len(missing_results),
            "MissingCaseCount": len(missing_cases),
            "PrivacyLeakCount": len(privacy_errors),
            "RawPathLeakCount": raw_path_leak_count,
            "ThresholdGatePassed": threshold_gate_passed,
            "Accepted": accepted,
            "ProofStatus": operator_row["proofStatus"],
            "Errors": errors,
            "PrivacyErrors": privacy_errors,
        },
        "Operators": [operator_row],
    }

    if dry_run:
        print(
            f"[{operator}] dry-run proof: test={len(per_case_rows)} passed={aggregate['Passed']} "
            f"failed={aggregate['Failed']} accepted={accepted}"
        )
    else:
        write_json(field_algorithm_proof_baseline_path(operator), baseline)
        field_algorithm_proof_report_path(operator).write_text(render_report(baseline), encoding="utf-8", newline="\n")
        update_aggregate_baseline(operator, operator_row)
        update_registry(operator, operator_row)
        print(
            f"[{operator}] field algorithm proof written: test={len(per_case_rows)} "
            f"passed={aggregate['Passed']} failed={aggregate['Failed']} status={operator_row['proofStatus']}"
        )

    if not accepted:
        for error in errors + privacy_errors:
            print(f"error: {error}")
        return 2
    return 0


def parse_operators(args: argparse.Namespace) -> list[str]:
    values: list[str] = []
    if args.all_pilots:
        values.extend(PILOT_OPERATORS)
    if args.operator:
        values.extend(args.operator)
    if args.operators:
        values.extend(args.operators)
    if not values:
        values.extend(PILOT_OPERATORS)
    unique: list[str] = []
    for value in values:
        if value not in unique:
            unique.append(value)
    for value in unique:
        if value not in PILOT_OPERATORS:
            raise ProofError(f"{value} is not enabled for pilot field algorithm proof yet")
    return unique


def main() -> int:
    parser = argparse.ArgumentParser(description="Run Core20 pilot field algorithm proof against frozen test splits.")
    parser.add_argument("--operator", action="append", help="Pilot operator to prove. Repeatable.")
    parser.add_argument("--operators", nargs="*", help="Pilot operators to prove.")
    parser.add_argument("--all-pilots", action="store_true", help="Run all pilot operators.")
    parser.add_argument("--dataset-root", help="Override CLEARVISION_PRODUCTION_DATASET_ROOT.")
    parser.add_argument("--results-file", help="Override result filename/path. Relative paths resolve under field_v1.")
    parser.add_argument("--allow-missing-data", action="store_true", help="Return success when field data or proof results are not available yet.")
    parser.add_argument("--validate-config-only", action="store_true", help="Validate repo-side pilot proof configuration only.")
    parser.add_argument("--dry-run", action="store_true", help="Validate proof without writing baseline/report files.")
    args = parser.parse_args()

    try:
        operators = parse_operators(args)
        if args.validate_config_only:
            validate_repo_config(operators)
            print(f"field algorithm proof config valid: operators={','.join(operators)}")
            return 0
        dataset_root = resolve_dataset_root(args.dataset_root)
        status = 0
        for operator in operators:
            result = run_operator(operator, dataset_root, args.results_file, args.dry_run, args.allow_missing_data)
            status = max(status, result)
        return status
    except (ProofError, IngestError) as error:
        print(f"error: {error}")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
