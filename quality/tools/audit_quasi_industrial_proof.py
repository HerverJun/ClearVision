from __future__ import annotations

import json
import re
import hashlib
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
SUITE_DIR = REPO_ROOT / "quality" / "evals" / "suites"
DATASET_DIR = REPO_ROOT / "quality" / "datasets"
GOVERNED_CATALOG = REPO_ROOT / "docs" / "算子资料" / "算子名片" / "catalog.json"
RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/)")
ALLOWED_PROOF_LEVELS = {"missing", "contract", "golden", "public-benchmark", "field-substitute", "real-field"}
PUBLIC_PROOF_SCHEMA_VERSION = "2026-04-29.public-benchmark-proof.v1"
PUBLIC_REPLAY_SCHEMA_VERSION = "2026-04-29.public-benchmark-replay.v1"
ALLOWED_REPLAY_COMMANDS = {
    ("python", "quality/tools/run_algorithm_ab_replay.py", "--execute-camera-calibration"),
    ("python", "quality/tools/run_algorithm_ab_replay.py", "--execute-matching"),
}
REQUIRED_RUNNER_FIELDS = {
    "datasetId",
    "manifestSha256",
    "splitSummary",
    "metrics",
    "thresholdResults",
    "perCaseResults",
    "failureTaxonomy",
    "privacyLeakCount",
    "accepted",
}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def repo(path: Path) -> str:
    return path.relative_to(REPO_ROOT).as_posix()


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def strict_int(value: Any) -> int | None:
    if isinstance(value, int) and not isinstance(value, bool):
        return value
    return None


def normalize_sha256(value: Any) -> str:
    text = str(value or "").strip()
    if text.startswith("sha256:"):
        text = text[len("sha256:") :]
    return text


def is_sha256_hex(value: str) -> bool:
    return bool(re.fullmatch(r"[0-9a-fA-F]{64}", value))


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def add_check(checks: list[dict[str, Any]], check_id: str, passed: bool, details: str) -> None:
    checks.append({"id": check_id, "passed": passed, "details": details})


def inspect_registry(checks: list[dict[str, Any]], registry: dict[str, Any]) -> None:
    summary = registry.get("summary", {})
    operators = registry.get("operators", [])
    catalog = read_json(GOVERNED_CATALOG)
    population = catalog.get("population", {})
    expected_count = population.get("formalTotal")
    add_check(
        checks,
        "registry_operator_count_matches_governed_population",
        summary.get("operatorCount") == expected_count == len(operators),
        f"registry={summary.get('operatorCount')} formalTotal={expected_count}",
    )
    add_check(
        checks,
        "registry_population_identity",
        registry.get("population") == population and registry.get("populationDelta") == catalog.get("populationDelta"),
        str(population.get("fingerprint")),
    )
    governed_ids = {str(item.get("id")) for item in catalog.get("operators", []) if isinstance(item, dict)}
    registry_ids = {str(item.get("operator")) for item in operators if isinstance(item, dict)}
    add_check(
        checks,
        "registry_operator_identities_match_governed_catalog",
        registry_ids == governed_ids,
        f"registry={len(registry_ids)} governed={len(governed_ids)}",
    )
    add_check(checks, "registry_real_field_zero", summary.get("realIndustrialValidationComplete") == 0, str(summary.get("realIndustrialValidationComplete")))
    add_check(checks, "registry_core20_count", summary.get("core20Count") == 20, str(summary.get("core20Count")))

    invalid_levels = [
        row.get("operator")
        for row in operators
        if row.get("currentProofLevel") not in ALLOWED_PROOF_LEVELS
        or row.get("targetMinimumProofLevel") not in ALLOWED_PROOF_LEVELS
    ]
    add_check(checks, "registry_proof_levels_allowed", not invalid_levels, ", ".join(map(str, invalid_levels[:10])))

    overclaims = []
    for row in operators:
        claim = str(row.get("evidenceClaim", "")).strip().lower()
        status = str(row.get("industrialStatus", "")).strip().lower()
        if row.get("currentProofLevel") == "real-field" or claim == "real field proof" or status == "real industrial validation complete":
            overclaims.append(row.get("operator"))
    add_check(checks, "registry_no_real_field_overclaim", not overclaims, ", ".join(map(str, overclaims[:10])))

    missing_dataset_plan = [row.get("operator") for row in operators if not row.get("recommendedDatasets")]
    add_check(checks, "registry_all_rows_have_dataset_strategy", not missing_dataset_plan, ", ".join(map(str, missing_dataset_plan[:10])))

    invalid_legacy_disposition = [
        row.get("operator")
        for row in operators
        if row.get("legacyBaselineDisposition") not in {"legacy-evidence-only", "no-baseline-evidence"}
    ]
    add_check(
        checks,
        "registry_legacy_baselines_downgraded",
        not invalid_legacy_disposition,
        ", ".join(map(str, invalid_legacy_disposition[:10])),
    )

    required_fields = set(registry.get("proofModel", {}).get("requiredRunnerFields", []))
    add_check(
        checks,
        "registry_runner_schema_complete",
        REQUIRED_RUNNER_FIELDS.issubset(required_fields),
        ", ".join(sorted(REQUIRED_RUNNER_FIELDS - required_fields)),
    )

    add_check(
        checks,
        "registry_no_raw_path",
        RAW_PATH_RE.search(json.dumps(registry, ensure_ascii=False)) is None,
        "registry raw path scan",
    )


def inspect_public_datasets(checks: list[dict[str, Any]], dataset_cards: dict[str, Any]) -> None:
    datasets = dataset_cards.get("datasets", [])
    add_check(checks, "public_dataset_cards_present", len(datasets) >= 6, str(len(datasets)))
    missing_license = [item.get("datasetId") for item in datasets if not item.get("license") or not item.get("sourceUrl")]
    add_check(checks, "public_dataset_license_source_present", not missing_license, ", ".join(map(str, missing_license)))
    planned = [item.get("datasetId") for item in datasets if item.get("status") == "planned"]
    allowed_statuses = {"planned", "available-local", "downloaded-index-pending"}
    invalid_statuses = [
        f"{item.get('datasetId')}={item.get('status')}"
        for item in datasets
        if item.get("status") not in allowed_statuses
    ]
    add_check(
        checks,
        "public_dataset_planned_items_explicit",
        not invalid_statuses,
        f"planned={','.join(map(str, planned))}; invalid={','.join(invalid_statuses)}",
    )
    add_check(
        checks,
        "public_dataset_cards_no_raw_path",
        RAW_PATH_RE.search(json.dumps(dataset_cards, ensure_ascii=False)) is None,
        "dataset card raw path scan",
    )


def inspect_public_benchmark_proof(checks: list[dict[str, Any]]) -> None:
    proof_path = REPORT_DIR / "QualityFlywheel_public_benchmark_proof_baseline.json"
    proof_summary_path = REPORT_DIR / "QualityFlywheel_public_benchmark_proof_baseline.summary.json"
    retained_summary = False
    source_proof_sha: str | None = None
    if not proof_path.exists():
        if not proof_summary_path.exists():
            add_check(checks, "public_benchmark_proof_exists", False, "quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.json")
            return
        proof = read_json(proof_summary_path)
        retained_summary = True
        source_report = proof.get("sourceReport", {}) if isinstance(proof.get("sourceReport"), dict) else {}
        original_sha = normalize_sha256(source_report.get("originalSha256"))
        if is_sha256_hex(original_sha):
            source_proof_sha = original_sha
        add_check(
            checks,
            "public_benchmark_proof_exists",
            source_report.get("originalPath") == "quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.json",
            repo(proof_summary_path),
        )
        add_check(
            checks,
            "public_benchmark_proof_retained_summary",
            proof.get("schemaVersion") == "quality-report-summary/v1"
            and "removed-from-git" in str(source_report.get("retentionDecision") or "")
            and is_sha256_hex(original_sha)
            and (strict_int(source_report.get("originalSizeBytes")) or 0) > 0,
            str(source_report.get("retentionDecision")),
        )
    else:
        proof = read_json(proof_path)
        source_proof_sha = sha256_file(proof_path)
        add_check(checks, "public_benchmark_proof_exists", True, "quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.json")
        add_check(
            checks,
            "public_benchmark_proof_schema_version",
            proof.get("schemaVersion") == PUBLIC_PROOF_SCHEMA_VERSION,
            str(proof.get("schemaVersion")),
        )

    operators = proof.get("operators", [])
    if not isinstance(operators, list):
        operators = []
    proof_summary = proof.get("summary") if isinstance(proof.get("summary"), dict) else {}
    summary_operator_count = strict_int(proof_summary.get("operatorCount"))
    summary_accepted_count = strict_int(proof_summary.get("acceptedCount"))
    summary_failed_count = strict_int(proof_summary.get("failedCount"))
    summary_replay_case_count = strict_int(proof_summary.get("replayCaseCount"))
    expected_proof_accepted = bool(operators) and all(
        isinstance(row, dict) and row.get("accepted") is True for row in operators
    )
    add_check(
        checks,
        "public_benchmark_proof_disposition_consistent",
        proof.get("accepted") is expected_proof_accepted,
        f"declared={proof.get('accepted')} expected={expected_proof_accepted}",
    )
    add_check(checks, "public_benchmark_proof_operator_count", len(operators) >= 8, str(len(operators)))
    add_check(
        checks,
        "public_benchmark_proof_summary_counts_consistent",
        summary_operator_count == len(operators)
        and summary_accepted_count == sum(1 for row in operators if isinstance(row, dict) and row.get("accepted") is True)
        and summary_failed_count == sum(1 for row in operators if isinstance(row, dict) and row.get("accepted") is not True),
        f"operators={summary_operator_count}/{len(operators)} accepted={summary_accepted_count} failed={summary_failed_count}",
    )

    missing_fields = []
    operator_replay_case_count = 0
    for row in operators:
        if not isinstance(row, dict):
            missing_fields.append("<non-object>: row")
            continue
        if retained_summary:
            missing = {
                "accepted",
                "datasetId",
                "failureTaxonomy",
                "manifestSha256",
                "metrics",
                "perCaseResultCount",
                "privacyLeakCount",
                "splitSummary",
                "thresholdResultCount",
            } - set(row.keys())
            per_case_count = strict_int(row.get("perCaseResultCount"))
            threshold_count = strict_int(row.get("thresholdResultCount"))
            replay_count = strict_int(row.get("replayCaseCount"))
            if per_case_count is None or per_case_count <= 0:
                missing.add("positivePerCaseResultCount")
            if threshold_count is None or threshold_count <= 0:
                missing.add("positiveThresholdResultCount")
            if replay_count is None or replay_count <= 0:
                missing.add("positiveReplayCaseCount")
            else:
                operator_replay_case_count += replay_count
        else:
            missing = REQUIRED_RUNNER_FIELDS - set(row.keys())
            replay_cases = row.get("replayCases")
            operator_replay_case_count += len(replay_cases) if isinstance(replay_cases, list) else 0
        if missing:
            missing_fields.append(f"{row.get('operator')}: {','.join(sorted(missing))}")
    add_check(checks, "public_benchmark_proof_schema_complete", not missing_fields, "; ".join(missing_fields[:5]))
    add_check(
        checks,
        "public_benchmark_proof_replay_count_consistent",
        summary_replay_case_count == operator_replay_case_count,
        f"{summary_replay_case_count}/{operator_replay_case_count}",
    )

    overclaims = [
        row.get("operator")
        for row in operators
        if row.get("proofLevel") == "real-field"
        or str(row.get("industrialStatus", "")).strip().lower() == "real industrial validation complete"
    ]
    add_check(checks, "public_benchmark_proof_no_real_field_overclaim", not overclaims, ", ".join(map(str, overclaims[:10])))

    privacy_leaks = [row.get("operator") for row in operators if row.get("privacyLeakCount") != 0]
    add_check(checks, "public_benchmark_proof_privacy_clean", not privacy_leaks, ", ".join(map(str, privacy_leaks[:10])))
    add_check(
        checks,
        "public_benchmark_proof_no_raw_path",
        RAW_PATH_RE.search(json.dumps(proof, ensure_ascii=False)) is None,
        "public benchmark proof raw path scan",
    )
    deep_learning_rows = [row for row in operators if row.get("operator") == "DeepLearning"]
    deep_learning_row = next(iter(deep_learning_rows), None)
    if deep_learning_row is None:
        add_check(checks, "public_benchmark_proof_deeplearning_baseline", False, "DeepLearning row missing")
    else:
        add_check(
            checks,
            "public_benchmark_proof_deeplearning_baseline",
            deep_learning_row.get("sourceBaseline") == "quality/evals/reports/DeepLearning_coco_real_model_baseline.json",
            str(deep_learning_row.get("sourceBaseline")),
        )
        add_check(
            checks,
            "public_benchmark_proof_deeplearning_no_annotation_seeded_claim",
            "annotation-seeded" not in str(deep_learning_row.get("evidenceClaim", "")).lower(),
            str(deep_learning_row.get("evidenceClaim")),
        )
        if deep_learning_row.get("proofLevel") == "inference-smoke-only":
            blocking_reasons = deep_learning_row.get("precisionBlockingReasons", [])
            metrics = deep_learning_row.get("metrics", {})
            smoke_truthful = (
                deep_learning_row.get("accepted") is False
                and deep_learning_row.get("precisionDisposition") == "FAIL"
                and isinstance(blocking_reasons, list)
                and "INFERENCE_SMOKE_ONLY" in blocking_reasons
                and isinstance(metrics, dict)
                and all(metrics.get(metric) == 0 for metric in ("AP50", "PrecisionAt50", "RecallAt50"))
            )
            add_check(
                checks,
                "public_benchmark_proof_deeplearning_smoke_truthful",
                smoke_truthful,
                f"accepted={deep_learning_row.get('accepted')} disposition={deep_learning_row.get('precisionDisposition')}",
            )

    replay_path = REPORT_DIR / "QualityFlywheel_public_benchmark_replay_manifest.json"
    if not replay_path.exists():
        add_check(checks, "public_benchmark_replay_manifest_exists", False, repo(replay_path))
        return
    replay = read_json(replay_path)
    replay_cases = replay.get("cases", [])
    if not isinstance(replay_cases, list):
        replay_cases = []
    replay_summary = replay.get("summary") if isinstance(replay.get("summary"), dict) else {}
    replay_source_sha = normalize_sha256(replay.get("sourceProofSha256"))
    replay_class_counts = Counter(str(case.get("replayClass")) for case in replay_cases if isinstance(case, dict))
    replay_operator_count = len({case.get("operator") for case in replay_cases if isinstance(case, dict)})
    replay_summary_case_count = strict_int(replay_summary.get("replayCaseCount"))
    replay_summary_operator_count = strict_int(replay_summary.get("operatorCount"))
    add_check(checks, "public_benchmark_replay_manifest_exists", True, repo(replay_path))
    add_check(
        checks,
        "public_benchmark_replay_schema_version",
        replay.get("schemaVersion") == PUBLIC_REPLAY_SCHEMA_VERSION,
        str(replay.get("schemaVersion")),
    )
    add_check(
        checks,
        "public_benchmark_replay_source_baseline",
        replay.get("sourceProofBaseline") == "quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.json",
        str(replay.get("sourceProofBaseline")),
    )
    add_check(
        checks,
        "public_benchmark_replay_source_sha_matches_proof",
        source_proof_sha is not None and replay_source_sha == source_proof_sha,
        f"{replay_source_sha}/{source_proof_sha}",
    )
    add_check(checks, "public_benchmark_replay_manifest_accepted", replay.get("accepted") is True, str(replay.get("accepted")))
    add_check(checks, "public_benchmark_replay_has_cases", len(replay_cases) >= len(operators), str(len(replay_cases)))
    add_check(
        checks,
        "public_benchmark_replay_summary_counts_consistent",
        replay_summary_case_count == len(replay_cases)
        and replay_summary_case_count == summary_replay_case_count
        and replay_summary_operator_count == replay_operator_count
        and replay_summary_operator_count == summary_operator_count,
        f"cases={replay_summary_case_count}/{len(replay_cases)}/{summary_replay_case_count} "
        f"operators={replay_summary_operator_count}/{replay_operator_count}/{summary_operator_count}",
    )
    add_check(
        checks,
        "public_benchmark_replay_class_counts_consistent",
        replay_summary.get("classCounts") == dict(sorted(replay_class_counts.items())),
        str(replay_summary.get("classCounts")),
    )
    missing_triage = [
        case.get("caseId")
        for case in replay_cases
        if isinstance(case, dict) and (not case.get("triageLabel") or not case.get("replayCommand"))
    ]
    add_check(checks, "public_benchmark_replay_triage_complete", not missing_triage, ", ".join(map(str, missing_triage[:10])))
    invalid_replay_commands = []
    invalid_replay_classes = []
    for index, case in enumerate(replay_cases):
        if not isinstance(case, dict):
            invalid_replay_commands.append(f"{index}: non-object")
            continue
        replay_class = case.get("replayClass")
        if replay_class not in {"boundary", "failure"}:
            invalid_replay_classes.append(str(case.get("caseId") or index))
        replay_command = case.get("replayCommand")
        if not isinstance(replay_command, list) or any(not isinstance(item, str) for item in replay_command):
            invalid_replay_commands.append(str(case.get("caseId") or index))
            continue
        command_tuple = tuple(replay_command)
        if command_tuple not in ALLOWED_REPLAY_COMMANDS:
            invalid_replay_commands.append(str(case.get("caseId") or index))
        elif case.get("operator") == "CameraCalibration" and replay_command[-1] != "--execute-camera-calibration":
            invalid_replay_commands.append(str(case.get("caseId") or index))
        elif case.get("operator") != "CameraCalibration" and replay_command[-1] != "--execute-matching":
            invalid_replay_commands.append(str(case.get("caseId") or index))
    add_check(
        checks,
        "public_benchmark_replay_classes_allowed",
        not invalid_replay_classes,
        ", ".join(invalid_replay_classes[:10]),
    )
    add_check(
        checks,
        "public_benchmark_replay_commands_allowed",
        not invalid_replay_commands,
        ", ".join(invalid_replay_commands[:10]),
    )
    add_check(
        checks,
        "public_benchmark_replay_no_raw_path",
        RAW_PATH_RE.search(json.dumps(replay, ensure_ascii=False)) is None,
        "public benchmark replay raw path scan",
    )


def inspect_suites(checks: list[dict[str, Any]]) -> None:
    suite_names = ("public_benchmark_suite", "full155_quality_suite", "algorithm_improvement_suite", "audit_suite")
    missing = [name for name in suite_names if not (SUITE_DIR / f"{name}.json").exists()]
    add_check(checks, "required_suites_exist", not missing, ", ".join(missing))
    for suite_name in suite_names:
        path = SUITE_DIR / f"{suite_name}.json"
        if not path.exists():
            continue
        suite = read_json(path)
        entries = [
            entry
            for stage in suite.get("stages", [])
            if isinstance(stage, dict)
            for entry in stage.get("entries", [])
            if isinstance(entry, dict)
        ]
        active = [entry for entry in entries if entry.get("status") == "active"]
        add_check(checks, f"{suite_name}_has_active_entry", bool(active), str(len(active)))
        raw = json.dumps(suite, ensure_ascii=False)
        add_check(checks, f"{suite_name}_no_raw_path", RAW_PATH_RE.search(raw) is None, "suite raw path scan")


def inspect_algorithm_ab_report(checks: list[dict[str, Any]]) -> None:
    path = REPORT_DIR / "QualityFlywheel_algorithm_ab_replay_report.json"
    if not path.exists():
        add_check(checks, "algorithm_ab_replay_report_exists", False, repo(path))
        return

    report = read_json(path)
    rows = report.get("operators", [])
    add_check(checks, "algorithm_ab_replay_report_exists", True, repo(path))
    add_check(checks, "algorithm_ab_replay_report_accepted", report.get("accepted") is True, str(report.get("accepted")))
    add_check(checks, "algorithm_ab_replay_report_has_replay_cases", report.get("summary", {}).get("replayCaseCount", 0) >= 100, str(report.get("summary", {}).get("replayCaseCount")))
    add_check(checks, "algorithm_ab_replay_report_no_pending_candidates", report.get("summary", {}).get("candidatePendingCount") == 0, str(report.get("summary", {}).get("candidatePendingCount")))
    add_check(
        checks,
        "algorithm_ab_replay_report_all_cases_compared",
        report.get("summary", {}).get("comparedCaseCount") == report.get("summary", {}).get("replayCaseCount"),
        f"{report.get('summary', {}).get('comparedCaseCount')}/{report.get('summary', {}).get('replayCaseCount')}",
    )
    add_check(
        checks,
        "algorithm_ab_replay_report_candidate_executed_ge_160",
        report.get("summary", {}).get("executedCandidateCaseCount", 0) >= 160,
        str(report.get("summary", {}).get("executedCandidateCaseCount")),
    )
    add_check(
        checks,
        "algorithm_ab_replay_report_regressed_zero",
        report.get("summary", {}).get("regressedCaseCount", 0) == 0,
        str(report.get("summary", {}).get("regressedCaseCount")),
    )
    deep_learning_rows = [row for row in rows if row.get("operator") == "DeepLearning"]
    if not deep_learning_rows:
        add_check(checks, "algorithm_ab_replay_report_deeplearning_row_present", False, "DeepLearning row missing")
    else:
        deep_learning_row = deep_learning_rows[0]
        deep_learning_status = str(deep_learning_row.get("comparisonStatus") or "")
        add_check(
            checks,
            "algorithm_ab_replay_report_deeplearning_candidate_or_control",
            deep_learning_status in {"candidate-executed", "unchanged-baseline-control"},
            deep_learning_status,
        )
        add_check(
            checks,
            "algorithm_ab_replay_report_deeplearning_cases_ge_20",
            report.get("summary", {}).get("deepLearningCaseCount", 0) >= 20,
            str(report.get("summary", {}).get("deepLearningCaseCount")),
        )
        add_check(
            checks,
            "algorithm_ab_replay_report_deeplearning_processing_errors_zero",
            report.get("summary", {}).get("deepLearningProcessingErrorCaseCount", 0) == 0,
            str(report.get("summary", {}).get("deepLearningProcessingErrorCaseCount")),
        )
        if deep_learning_status == "candidate-executed":
            add_check(
                checks,
                "algorithm_ab_replay_report_deeplearning_real_model_cases_ge_20",
                report.get("summary", {}).get("deepLearningRealModelCaseCount", 0) >= 20,
                str(report.get("summary", {}).get("deepLearningRealModelCaseCount")),
            )
            add_check(
                checks,
                "algorithm_ab_replay_report_deeplearning_candidate_summary_accessible",
                bool(deep_learning_row.get("candidateBaseline")),
                str(deep_learning_row.get("candidateBaseline")),
            )
            candidate_summary_path = REPO_ROOT / str(deep_learning_row.get("candidateBaseline"))
            candidate_report_available = candidate_summary_path.exists()
            add_check(
                checks,
                "algorithm_ab_replay_report_deeplearning_candidate_summary_exists",
                candidate_report_available,
                str(deep_learning_row.get("candidateBaseline")),
            )
            if candidate_report_available:
                candidate_report = read_json(candidate_summary_path)
                candidate_summary = candidate_report.get("Summary", {})
                add_check(
                    checks,
                    "algorithm_ab_replay_report_deeplearning_candidate_profile",
                    candidate_summary.get("Profile") == "real_model_hard_nms_045",
                    str(candidate_summary.get("Profile")),
                )
                add_check(
                    checks,
                    "algorithm_ab_replay_report_deeplearning_candidate_annotation_seeded_false",
                    candidate_summary.get("AnnotationSeeded") is False,
                    str(candidate_summary.get("AnnotationSeeded")),
                )
                add_check(
                    checks,
                    "algorithm_ab_replay_report_deeplearning_candidate_artifact_present",
                    str(candidate_summary.get("ModelArtifactRef", "")).strip() != "",
                    str(candidate_summary.get("ModelArtifactRef")),
                )
        else:
            add_check(
                checks,
                "algorithm_ab_replay_report_deeplearning_control_case_count",
                deep_learning_row.get("replayCaseCount") == report.get("summary", {}).get("deepLearningCaseCount"),
                f"{deep_learning_row.get('replayCaseCount')}/{report.get('summary', {}).get('deepLearningCaseCount')}",
            )
    add_check(
        checks,
        "algorithm_ab_replay_report_camera_cases_ge_3",
        report.get("summary", {}).get("cameraCalibrationCaseCount", 0) >= 3,
        str(report.get("summary", {}).get("cameraCalibrationCaseCount")),
    )
    add_check(
        checks,
        "algorithm_ab_replay_report_camera_regressed_zero",
        report.get("summary", {}).get("cameraCalibrationRegressedCaseCount", 0) == 0,
        str(report.get("summary", {}).get("cameraCalibrationRegressedCaseCount")),
    )
    add_check(
        checks,
        "algorithm_ab_replay_report_camera_worse_metric_zero",
        report.get("summary", {}).get("cameraCalibrationWorseMetricCaseCount", 0) == 0,
        str(report.get("summary", {}).get("cameraCalibrationWorseMetricCaseCount")),
    )
    missing_replay = [row.get("operator") for row in rows if row.get("replayCaseCount", 0) <= 0]
    add_check(checks, "algorithm_ab_replay_report_all_ops_wired", not missing_replay, ", ".join(map(str, missing_replay[:10])))
    add_check(
        checks,
        "algorithm_ab_replay_report_no_raw_path",
        RAW_PATH_RE.search(json.dumps(report, ensure_ascii=False)) is None,
        "algorithm A/B report raw path scan",
    )


def build_report(checks: list[dict[str, Any]]) -> dict[str, Any]:
    passed = all(check["passed"] for check in checks)
    return {
        "schemaVersion": "2026-04-29.quasi-industrial-audit.v1",
        "generatedAtUtc": utc_now(),
        "passed": passed,
        "summary": {
            "checkCount": len(checks),
            "passedCount": sum(1 for check in checks if check["passed"]),
            "failedCount": sum(1 for check in checks if not check["passed"]),
            "realIndustrialValidationComplete": 0,
        },
        "checks": checks,
        "policy": {
            "claimBoundary": "Public benchmark, semisynthetic, and field-substitute proof may support quasi-industrial claims only.",
            "realFieldRule": "Do not claim real industrial validation complete without own production data and site/line sign-off.",
        },
    }


def render_markdown(report: dict[str, Any]) -> str:
    lines = [
        "# Quality Flywheel Governed-Population Quasi-Industrial Audit",
        "",
        f"GeneratedAtUtc: `{report['generatedAtUtc']}`",
        f"Passed: `{'Yes' if report['passed'] else 'No'}`",
        "",
        "## Summary",
        "",
        f"- Checks: {report['summary']['checkCount']}",
        f"- Passed: {report['summary']['passedCount']}",
        f"- Failed: {report['summary']['failedCount']}",
        "- Real industrial validation complete: 0",
        "",
        "## Checks",
        "",
        "| Check | Status | Details |",
        "|---|---|---|",
    ]
    for check in report["checks"]:
        lines.append(f"| {check['id']} | {'Pass' if check['passed'] else 'Fail'} | {check['details']} |")
    lines.extend(
        [
            "",
            "## Claim Boundary",
            "",
            report["policy"]["claimBoundary"],
            report["policy"]["realFieldRule"],
            "",
        ]
    )
    return "\n".join(lines)


def main() -> int:
    checks: list[dict[str, Any]] = []
    registry_path = REPORT_DIR / "QualityFlywheel_155_quasi_industrial_registry.json"
    dataset_cards_path = DATASET_DIR / "public_benchmark_dataset_cards.json"

    if not registry_path.exists():
        add_check(checks, "registry_exists", False, repo(registry_path))
    else:
        add_check(checks, "registry_exists", True, repo(registry_path))
        inspect_registry(checks, read_json(registry_path))

    if not dataset_cards_path.exists():
        add_check(checks, "public_dataset_cards_exists", False, repo(dataset_cards_path))
    else:
        add_check(checks, "public_dataset_cards_exists", True, repo(dataset_cards_path))
        inspect_public_datasets(checks, read_json(dataset_cards_path))

    inspect_public_benchmark_proof(checks)
    inspect_algorithm_ab_report(checks)
    inspect_suites(checks)
    report = build_report(checks)
    write_json(REPORT_DIR / "QualityFlywheel_155_quasi_industrial_audit.json", report)
    (REPORT_DIR / "QualityFlywheel_155_quasi_industrial_audit.md").write_text(
        render_markdown(report), encoding="utf-8", newline="\n"
    )
    print(f"quasi-industrial audit passed={report['passed']} checks={report['summary']['checkCount']}")
    return 0 if report["passed"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
