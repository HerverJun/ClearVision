from __future__ import annotations

import json
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
SUITE_DIR = REPO_ROOT / "quality" / "evals" / "suites"
DATASET_DIR = REPO_ROOT / "quality" / "datasets"
RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/)")
ALLOWED_PROOF_LEVELS = {"missing", "contract", "golden", "public-benchmark", "field-substitute", "real-field"}
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


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def add_check(checks: list[dict[str, Any]], check_id: str, passed: bool, details: str) -> None:
    checks.append({"id": check_id, "passed": passed, "details": details})


def inspect_registry(checks: list[dict[str, Any]], registry: dict[str, Any]) -> None:
    summary = registry.get("summary", {})
    operators = registry.get("operators", [])
    add_check(checks, "registry_operator_count_155", summary.get("operatorCount") == 155, str(summary.get("operatorCount")))
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
    unexpected_planned = [dataset_id for dataset_id in planned if dataset_id not in {"coco2017", "hpatches"}]
    add_check(
        checks,
        "public_dataset_planned_items_explicit",
        not unexpected_planned,
        ", ".join(map(str, planned)),
    )
    add_check(
        checks,
        "public_dataset_cards_no_raw_path",
        RAW_PATH_RE.search(json.dumps(dataset_cards, ensure_ascii=False)) is None,
        "dataset card raw path scan",
    )


def inspect_public_benchmark_proof(checks: list[dict[str, Any]]) -> None:
    proof_path = REPORT_DIR / "QualityFlywheel_public_benchmark_proof_baseline.json"
    if not proof_path.exists():
        add_check(checks, "public_benchmark_proof_exists", False, "quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.json")
        return

    proof = read_json(proof_path)
    operators = proof.get("operators", [])
    add_check(checks, "public_benchmark_proof_exists", True, "quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.json")
    add_check(checks, "public_benchmark_proof_accepted", proof.get("accepted") is True, str(proof.get("accepted")))
    add_check(checks, "public_benchmark_proof_operator_count", len(operators) >= 8, str(len(operators)))

    missing_fields = []
    for row in operators:
        missing = REQUIRED_RUNNER_FIELDS - set(row.keys())
        if missing:
            missing_fields.append(f"{row.get('operator')}: {','.join(sorted(missing))}")
    add_check(checks, "public_benchmark_proof_schema_complete", not missing_fields, "; ".join(missing_fields[:5]))

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

    replay_path = REPORT_DIR / "QualityFlywheel_public_benchmark_replay_manifest.json"
    if not replay_path.exists():
        add_check(checks, "public_benchmark_replay_manifest_exists", False, repo(replay_path))
        return
    replay = read_json(replay_path)
    replay_cases = replay.get("cases", [])
    add_check(checks, "public_benchmark_replay_manifest_exists", True, repo(replay_path))
    add_check(checks, "public_benchmark_replay_manifest_accepted", replay.get("accepted") is True, str(replay.get("accepted")))
    add_check(checks, "public_benchmark_replay_has_cases", len(replay_cases) >= len(operators), str(len(replay_cases)))
    missing_triage = [
        case.get("caseId")
        for case in replay_cases
        if not case.get("triageLabel") or not case.get("replayCommand")
    ]
    add_check(checks, "public_benchmark_replay_triage_complete", not missing_triage, ", ".join(map(str, missing_triage[:10])))
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
        "algorithm_ab_replay_report_matching_candidate_executed",
        report.get("summary", {}).get("executedCandidateCaseCount", 0) >= 40,
        str(report.get("summary", {}).get("executedCandidateCaseCount")),
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
        "# Quality Flywheel 155 Quasi-Industrial Audit",
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
