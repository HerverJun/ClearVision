#!/usr/bin/env python3
"""Validate Vision Agent quality report artifacts before upload."""

from __future__ import annotations

import argparse
import json
import os
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORTS_DIR = REPO_ROOT / "quality" / "evals" / "reports"
TEST_RESULTS_DIR = REPO_ROOT / "test_results" / "agent_engineering_harness"
SCAN_POLICY_VERSION = "2026-06-07.runtime-preview-final-pre-pilot-hardening-scan.v1"

JSON_REPORTS = [
    REPORTS_DIR / "VisionAgent_business_benchmark_baseline.json",
    REPORTS_DIR / "planner_autonomy_benchmark.json",
    REPORTS_DIR / "runtime_preview_scenario_corpus.json",
    REPORTS_DIR / "runtime_preview_redacted_flow_corpus.json",
    REPORTS_DIR / "runtime_preview_redacted_flow_corpus_v2.json",
    REPORTS_DIR / "runtime_preview_scenario_evidence.json",
    REPORTS_DIR / "runtime_preview_deploy_readiness_report.sample.json",
    REPORTS_DIR / "runtime_preview_package_readiness_report.sample.json",
    REPORTS_DIR / "runtime_package_manifest_dry_run.sample.json",
    REPORTS_DIR / "runtime_preview_station_profiles_sample.json",
    REPORTS_DIR / "runtime_preview_operator_contract_registry.json",
    REPORTS_DIR / "runtime_preview_operator_contract_coverage.json",
    REPORTS_DIR / "runtime_preview_operator_contract_validation_sample.json",
    REPORTS_DIR / "runtime_preview_station_compatibility_dry_run.sample.json",
    REPORTS_DIR / "runtime_preview_pre_release_review_report.sample.json",
    REPORTS_DIR / "runtime_preview_governance_audit_sample.json",
    REPORTS_DIR / "runtime_preview_governance_export_sample.json",
    REPORTS_DIR / "runtime_preview_agent_explanation_benchmark.json",
    REPORTS_DIR / "runtime_preview_agent_explanation_v3.json",
    REPORTS_DIR / "runtime_preview_redacted_flow_corpus_final.json",
    REPORTS_DIR / "runtime_preview_station_profiles_final.json",
    REPORTS_DIR / "runtime_preview_operator_contract_registry_final.json",
    REPORTS_DIR / "runtime_preview_operator_contract_validation_final.json",
    REPORTS_DIR / "runtime_preview_station_compatibility_final.json",
    REPORTS_DIR / "runtime_package_manifest_dry_run_final.json",
    REPORTS_DIR / "runtime_preview_package_readiness_final.json",
    REPORTS_DIR / "runtime_preview_pre_release_review_final.json",
    REPORTS_DIR / "runtime_preview_release_decision_matrix.json",
    REPORTS_DIR / "runtime_preview_agent_explanation_final.json",
    REPORTS_DIR / "runtime_preview_governance_export_final.json",
    REPORTS_DIR / "runtime_preview_report_readability_gate.json",
    REPORTS_DIR / "real_llm_planner_shadow_eval.json",
    REPORTS_DIR / "real_llm_planner_shadow_eval.holdout.json",
]

MARKDOWN_REPORTS = [
    REPORTS_DIR / "VisionAgent_business_benchmark_baseline.md",
    REPORTS_DIR / "planner_autonomy_benchmark.md",
    REPORTS_DIR / "runtime_preview_scenario_corpus.md",
    REPORTS_DIR / "runtime_preview_redacted_flow_corpus.md",
    REPORTS_DIR / "runtime_preview_redacted_flow_corpus_v2.md",
    REPORTS_DIR / "runtime_preview_scenario_evidence.md",
    REPORTS_DIR / "runtime_preview_deploy_readiness_report.sample.md",
    REPORTS_DIR / "runtime_preview_package_readiness_report.sample.md",
    REPORTS_DIR / "runtime_package_manifest_dry_run.sample.md",
    REPORTS_DIR / "runtime_preview_station_profiles_sample.md",
    REPORTS_DIR / "runtime_preview_operator_contract_registry.md",
    REPORTS_DIR / "runtime_preview_operator_contract_coverage.md",
    REPORTS_DIR / "runtime_preview_operator_contract_validation_sample.md",
    REPORTS_DIR / "runtime_preview_station_compatibility_dry_run.sample.md",
    REPORTS_DIR / "runtime_preview_pre_release_review_report.sample.md",
    REPORTS_DIR / "runtime_preview_governance_audit_sample.md",
    REPORTS_DIR / "runtime_preview_governance_export_sample.md",
    REPORTS_DIR / "runtime_preview_agent_explanation_benchmark.md",
    REPORTS_DIR / "runtime_preview_agent_explanation_v3.md",
    REPORTS_DIR / "runtime_preview_redacted_flow_corpus_final.md",
    REPORTS_DIR / "runtime_preview_station_profiles_final.md",
    REPORTS_DIR / "runtime_preview_operator_contract_registry_final.md",
    REPORTS_DIR / "runtime_preview_operator_contract_validation_final.md",
    REPORTS_DIR / "runtime_preview_station_compatibility_final.md",
    REPORTS_DIR / "runtime_package_manifest_dry_run_final.md",
    REPORTS_DIR / "runtime_preview_package_readiness_final.md",
    REPORTS_DIR / "runtime_preview_pre_release_review_final.md",
    REPORTS_DIR / "runtime_preview_release_decision_matrix.md",
    REPORTS_DIR / "runtime_preview_agent_explanation_final.md",
    REPORTS_DIR / "runtime_preview_governance_export_final.md",
    REPORTS_DIR / "runtime_preview_report_readability_gate.md",
    REPORTS_DIR / "real_llm_planner_shadow_eval.md",
    REPORTS_DIR / "real_llm_planner_shadow_eval.holdout.md",
]

TEXT_OUTPUTS = [
    TEST_RESULTS_DIR / "agent_ui_contract_output.txt",
]

SOURCE_SCAN_EXTENSIONS = {
    ".cs",
    ".audit",
    ".export",
    ".manifest",
    ".js",
    ".json",
    ".jsonl",
    ".md",
    ".mjs",
    ".operator",
    ".ps1",
    ".py",
    ".report",
    ".review",
    ".session",
    ".station",
    ".txt",
    ".trx",
    ".yaml",
    ".yml",
}

SOURCE_SCAN_EXCLUDED_PARTS = {
    ".git",
    ".nuget",
    ".tmp",
    ".codex_tmp",
    ".vs",
    "bin",
    "obj",
    "node_modules",
    "packages",
    "test_results",
}

FORBIDDEN_SECRET_PATTERNS = [
    re.compile(pattern, re.IGNORECASE)
    for pattern in [
        r"CV_AGENT_REAL_LLM_API_KEY",
        r"Authorization\s*[:=]",
        r"Bearer\s+[A-Za-z0-9._~+/=-]{8,}",
        r"x-api-key\s*[:=]",
        r"api-key\s*[:=]",
        r'"apiKey"\s*:\s*"[^"]+"',
        r"api_key=[^&\s]+",
        r"access_token=[^&\s]+",
        r"data:image/[a-z0-9.+-]+;base64,[A-Za-z0-9+/=\r\n]{32,}",
        r"\b(?:\d{1,3}\.){3}\d{1,3}\b",
        r"\bBaseUrl\b\s*[:=]\s*['\"]?(?!<redacted)[^'\"\s,}]+",
        r"\.cvpkg\b",
    ]
]

AGENT_RUN_AUDIT_FILE_NAMES = {
    "agent_run_events.jsonl",
    "agent_run_summary.jsonl",
}

SOURCE_SECRET_PATTERNS = [
    (
        "Authorization bearer literal",
        re.compile(r"\bAuthorization\b\s*[:=]\s*['\"]?\s*Bearer\s+[A-Za-z0-9._~+/=-]{20,}", re.IGNORECASE),
    ),
    (
        "Bearer token literal",
        re.compile(r"\bBearer\s+[A-Za-z0-9._~+/=-]{24,}", re.IGNORECASE),
    ),
    (
        "x-api-key literal",
        re.compile(r"\bx-api-key\b\s*[:=]\s*['\"]?[A-Za-z0-9._~+/=-]{12,}", re.IGNORECASE),
    ),
    (
        "explicit API key environment assignment",
        re.compile(r"\b(?:CV_AGENT_(?:REAL_LLM|CPA)_API_KEY|CPA_API_KEY|OPENAI_API_KEY)\b\s*[:=]\s*['\"]?[A-Za-z0-9._~+/=-]{6,}", re.IGNORECASE),
    ),
    (
        "credential query parameter",
        re.compile(r"[?&](?:api[_-]?key|access[_-]?token|token|key)=[A-Za-z0-9._~+/=-]{12,}", re.IGNORECASE),
    ),
    (
        "base64 image payload",
        re.compile(r"data:image/[a-z0-9.+-]+;base64,[A-Za-z0-9+/=\r\n]{32,}", re.IGNORECASE),
    ),
    (
        "unredacted CGNAT CPA base URL",
        re.compile(r"https?://(?:[^/\s@]+@)?100\.(?:6[4-9]|[7-9]\d|1[01]\d|12[0-7])\.\d{1,3}\.\d{1,3}(?::\d+)?/[^\s`\"']*", re.IGNORECASE),
    ),
    (
        "unredacted CGNAT CPA v1 endpoint",
        re.compile(r"\b100\.(?:6[4-9]|[7-9]\d|1[01]\d|12[0-7])\.\d{1,3}\.\d{1,3}(?::\d+)?/v1\b", re.IGNORECASE),
    ),
]

UNREDACTED_BASE_URL_PATTERN = re.compile(
    r"https?://(?:[^/\s@]+@)?(?:\d{1,3}(?:\.\d{1,3}){3}|[A-Za-z0-9.-]+\.[A-Za-z]{2,})(?:/[^\s`\"']*)?(?:\?[^\s`\"']*)?",
    re.IGNORECASE,
)


def repo_relative(path: Path) -> str:
    try:
        return path.relative_to(REPO_ROOT).as_posix()
    except ValueError:
        return str(path)


def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        data = json.load(handle)
    if not isinstance(data, dict):
        raise ValueError(f"{repo_relative(path)} root must be a JSON object.")
    return data


def validate_workflow_run(
    path: Path,
    report: dict[str, Any],
    require_non_local: bool,
    errors: list[str],
) -> dict[str, str]:
    raw = report.get("workflowRun")
    if not isinstance(raw, dict):
        errors.append(f"{repo_relative(path)} missing workflowRun object.")
        return {}

    result: dict[str, str] = {}
    for field in ["commitSha", "branchName", "runId", "runAttempt", "generatedAtUtc"]:
        value = raw.get(field)
        if not isinstance(value, str) or not value.strip():
            errors.append(f"{repo_relative(path)} workflowRun.{field} must be a non-empty string.")
            continue
        result[field] = value.strip()
        if require_non_local and value.strip().lower() == "local":
            errors.append(f"{repo_relative(path)} workflowRun.{field} must not be local in CI artifacts.")

    return result


def validate_shadow_report(path: Path, report: dict[str, Any], errors: list[str]) -> None:
    text = path.read_text(encoding="utf-8")
    validate_no_secret_leaks(path, text, errors)

    config = report.get("llmConfiguration")
    if not isinstance(config, dict):
        errors.append(f"{repo_relative(path)} missing llmConfiguration object.")
        return

    for field in ["provider", "protocol", "wireApi", "authMode", "modelRole"]:
        value = config.get(field)
        if not isinstance(value, str) or not value.strip():
            errors.append(f"{repo_relative(path)} llmConfiguration.{field} must be a non-empty string.")

    base_url = config.get("baseUrl")
    if isinstance(base_url, str) and ("?" in base_url or "@" in base_url or "<redacted-host>" not in base_url):
        errors.append(f"{repo_relative(path)} llmConfiguration.baseUrl must be redacted.")
    for match in UNREDACTED_BASE_URL_PATTERN.finditer(text):
        value = match.group(0)
        if "<redacted-host>" not in value and not value.startswith("https://github.com"):
            errors.append(f"{repo_relative(path)} contains an unredacted URL-like BaseUrl: {value[:80]}")
            break

    summary = report.get("summary")
    if not isinstance(summary, dict):
        errors.append(f"{repo_relative(path)} missing summary object.")
        return

    for field in [
        "enabledReason",
        "skippedReason",
        "configurationMissingReason",
        "requestCount",
        "parseSuccessRate",
        "unsafeAttemptRate",
        "averageToolPlanMatchScore",
    ]:
        if field not in summary:
            errors.append(f"{repo_relative(path)} summary missing {field}.")


def validate_runtime_preview_case_counts(path: Path, report: dict[str, Any], errors: list[str]) -> None:
    summary = report.get("summary")
    if not isinstance(summary, dict):
        return

    minimums = {
        "VisionAgent_business_benchmark_baseline.json": 120,
        "runtime_preview_scenario_corpus.json": 60,
        "runtime_preview_redacted_flow_corpus.json": 60,
        "runtime_preview_redacted_flow_corpus_v2.json": 60,
        "runtime_preview_redacted_flow_corpus_final.json": 60,
        "runtime_preview_scenario_evidence.json": 60,
        "runtime_preview_package_readiness_report.sample.json": 60,
        "runtime_preview_package_readiness_final.json": 60,
        "runtime_package_manifest_dry_run.sample.json": 60,
        "runtime_package_manifest_dry_run_final.json": 60,
        "runtime_preview_station_compatibility_dry_run.sample.json": 60,
        "runtime_preview_station_compatibility_final.json": 60,
        "runtime_preview_operator_contract_validation_sample.json": 60,
        "runtime_preview_operator_contract_validation_final.json": 60,
        "runtime_preview_pre_release_review_report.sample.json": 60,
        "runtime_preview_pre_release_review_final.json": 60,
        "runtime_preview_release_decision_matrix.json": 60,
        "runtime_preview_agent_explanation_benchmark.json": 60,
        "runtime_preview_agent_explanation_v3.json": 60,
        "runtime_preview_agent_explanation_final.json": 60,
    }
    minimum = minimums.get(path.name)
    if minimum is None:
        return

    raw_count = summary.get("caseCount")
    if raw_count is None and path.name == "VisionAgent_business_benchmark_baseline.json":
        raw_count = report.get("caseCount")
    if not isinstance(raw_count, int) or raw_count < minimum:
        errors.append(f"{repo_relative(path)} must have caseCount >= {minimum}.")
    if summary.get("accepted") is not True:
        errors.append(f"{repo_relative(path)} summary.accepted must be true.")


def validate_runtime_preview_final_contract(path: Path, report: dict[str, Any], errors: list[str]) -> None:
    summary = report.get("summary")
    if not isinstance(summary, dict):
        errors.append(f"{repo_relative(path)} missing summary object.")
        return

    if path.name == "runtime_preview_operator_contract_coverage.json":
        coverage = report.get("coverageReport")
        if not isinstance(coverage, dict):
            errors.append(f"{repo_relative(path)} missing coverageReport object.")
            return
        if coverage.get("coveragePass") is not True or summary.get("coveragePass") is not True:
            errors.append(f"{repo_relative(path)} operator contract coverage must pass.")
        if not isinstance(coverage.get("coveredOperatorTypes"), list) or len(coverage["coveredOperatorTypes"]) < 16:
            errors.append(f"{repo_relative(path)} must cover at least 16 operator types.")
        if coverage.get("missingOperatorTypes"):
            errors.append(f"{repo_relative(path)} missingOperatorTypes must be empty.")

    if path.name in {"runtime_preview_station_profiles_sample.json", "runtime_preview_station_profiles_final.json"}:
        profiles = report.get("profiles")
        if not isinstance(profiles, list) or len(profiles) < 12:
            errors.append(f"{repo_relative(path)} must include at least 12 redacted station profiles.")
            return
        for profile in profiles:
            if profile.get("networkPolicy") != "redacted":
                errors.append(f"{repo_relative(path)} station profile networkPolicy must be redacted.")
            if profile.get("plcWriteAllowed") is not False:
                errors.append(f"{repo_relative(path)} station profile plcWriteAllowed must be false.")
            if not profile.get("approvalPolicy") or not profile.get("riskPolicy"):
                errors.append(f"{repo_relative(path)} station profile must include approvalPolicy and riskPolicy.")
                break

    if path.name in {"runtime_preview_pre_release_review_report.sample.json", "runtime_preview_pre_release_review_final.json"}:
        reports = report.get("reports")
        if not isinstance(reports, list) or not reports:
            errors.append(f"{repo_relative(path)} must include pre-release review reports.")
            return
        required_fields = [
            "reviewId",
            "caseId",
            "sessionId",
            "workflowDraftHash",
            "manifestId",
            "stationProfileId",
            "operatorContractVersion",
            "readinessStatus",
            "packageReviewAllowed",
            "stationCompatible",
            "operatorContractsSatisfied",
            "releaseReviewAllowed",
            "requiresEngineerApproval",
            "goNoGoDecision",
            "blockedReasons",
            "riskLevel",
            "engineerActions",
            "firstFixRecommendation",
            "workflowDraftAllowed",
            "decisionMatrix",
            "packageCreated",
            "deploymentExecuted",
            "realResourcesTouched",
        ]
        for item in reports[:10]:
            for field in required_fields:
                if field not in item:
                    errors.append(f"{repo_relative(path)} pre-release review missing {field}.")
                    return
            if item["packageCreated"] is not False or item["deploymentExecuted"] is not False or item["realResourcesTouched"] is not False:
                errors.append(f"{repo_relative(path)} pre-release review must stay metadata-only and non-deploying.")
                return

    if path.name == "runtime_preview_release_decision_matrix.json":
        decision_types = set(summary.get("decisionTypes") or [])
        required = {
            "releaseAllowed",
            "requiresEngineerApproval",
            "blocked",
            "forbiddenIntentDenied",
            "metadataIncomplete",
            "stationIncompatible",
            "operatorContractFailed",
            "manifestRiskBlocked",
            "packageReviewBlocked",
        }
        if decision_types != required:
            errors.append(f"{repo_relative(path)} decisionTypes must contain the full release readiness matrix.")
        for item in report.get("reports", [])[:10]:
            for decision_type in required:
                decision = item.get(decision_type)
                if not isinstance(decision, dict) or not decision.get("reason") or not decision.get("nextAction"):
                    errors.append(f"{repo_relative(path)} {decision_type} must include reason and nextAction.")
                    return

    if path.name == "runtime_preview_agent_explanation_final.json":
        for field in ["emptyStatusCount", "emptyDecisionCount", "emptyRiskCount", "emptyActionCount"]:
            if summary.get(field) != 0:
                errors.append(f"{repo_relative(path)} {field} must be zero.")
        for item in report.get("cases", [])[:10]:
            for field in ["status", "decision", "risk", "action", "firstFixRecommendation"]:
                if not item.get(field) or item.get(field) == "None":
                    errors.append(f"{repo_relative(path)} explanation field {field} must be readable.")
                    return

    if path.name == "runtime_preview_report_readability_gate.json":
        if summary.get("readabilityPass") is not True or summary.get("accepted") is not True:
            errors.append(f"{repo_relative(path)} readability gate must pass.")

    if path.name == "runtime_preview_governance_export_final.json":
        required_counts = [
            "releaseReviewDecisionCount",
            "stationProfileSnapshotCount",
            "operatorContractRegistrySnapshotCount",
            "operatorContractCoverageReportCount",
            "finalGovernanceExportCount",
        ]
        for field in required_counts:
            if not isinstance(summary.get(field), int) or summary[field] <= 0:
                errors.append(f"{repo_relative(path)} summary.{field} must be positive.")
        lookup_keys = set((report.get("exportManifest") or {}).get("lookupKeys") or [])
        for key in ["reviewId", "caseId", "manifestId", "stationProfileId", "operatorType", "reportId"]:
            if key not in lookup_keys:
                errors.append(f"{repo_relative(path)} exportManifest.lookupKeys missing {key}.")


def validate_no_secret_leaks(path: Path, text: str, errors: list[str]) -> None:
    for pattern in FORBIDDEN_SECRET_PATTERNS:
        if pattern.search(text):
            errors.append(f"{repo_relative(path)} leaks forbidden secret/auth fragment: {pattern.pattern}")


def validate_forbidden_fragments(path: Path, text: str, fragments: list[str], errors: list[str]) -> None:
    for fragment in fragments:
        if fragment and fragment in text:
            errors.append(f"{repo_relative(path)} leaks configured forbidden fragment.")


def collect_optional_agent_run_audit_files() -> list[Path]:
    candidates: list[Path] = []
    for env_name in ["CV_AGENT_RUN_EVENT_STORE", "CV_RUNTIME_PREVIEW_GOVERNANCE_STORE"]:
        value = os.environ.get(env_name, "")
        if not value:
            continue
        directory = Path(value)
        if not directory.is_absolute():
            directory = REPO_ROOT / directory
        for name in AGENT_RUN_AUDIT_FILE_NAMES:
            path = directory / name
            if path.exists():
                candidates.append(path)

    return sorted(set(candidates))


def validate_agent_run_jsonl(path: Path, text: str, errors: list[str]) -> None:
    if path.name not in AGENT_RUN_AUDIT_FILE_NAMES:
        return

    non_empty_lines = [line for line in text.splitlines() if line.strip()]
    if not non_empty_lines:
        errors.append(f"{repo_relative(path)} must contain at least one metadata-only AgentRun record.")
        return

    for index, line in enumerate(non_empty_lines, start=1):
        try:
            item = json.loads(line)
        except json.JSONDecodeError as exc:
            errors.append(f"{repo_relative(path)} line {index} is not valid JSONL: {exc}")
            return
        if item.get("metadataOnly") is not True:
            errors.append(f"{repo_relative(path)} line {index} must be metadataOnly=true.")
            return
        if item.get("redactionPass") is not True:
            errors.append(f"{repo_relative(path)} line {index} must be redactionPass=true.")
            return
        raw_lower = line.lower()
        for forbidden in ["chain_of_thought", "hidden_thought", "reasoning_content", "raw_prompt"]:
            if forbidden in raw_lower:
                errors.append(f"{repo_relative(path)} line {index} leaks hidden reasoning marker {forbidden}.")
                return


def iter_source_scan_files() -> list[Path]:
    files: list[Path] = []
    for root, dirs, names in os.walk(REPO_ROOT):
        dirs[:] = [name for name in dirs if name not in SOURCE_SCAN_EXCLUDED_PARTS]
        root_path = Path(root)
        for name in names:
            path = root_path / name
            if path.suffix.lower() not in SOURCE_SCAN_EXTENSIONS:
                continue
            files.append(path)
    return sorted(files)


def validate_source_secret_scan(files: list[Path], fragments: list[str], errors: list[str]) -> None:
    for path in files:
        text = path.read_text(encoding="utf-8", errors="ignore")
        validate_forbidden_fragments(path, text, fragments, errors)
        for label, pattern in SOURCE_SECRET_PATTERNS:
            if pattern.search(text):
                errors.append(f"{repo_relative(path)} leaks forbidden source credential pattern: {label}")


def build_manifest(
    json_reports: list[dict[str, Any]],
    files: list[Path],
    source_files_scanned: int,
    audit_reports_scanned: int,
    session_reports_scanned: int,
    forbidden_hit_count: int,
) -> dict[str, Any]:
    return {
        "schemaVersion": "2026-06-05.vision-agent-quality-artifact-manifest.v1",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "artifactName": "vision-agent-quality-suite",
        "scanPolicyVersion": SCAN_POLICY_VERSION,
        "sourceFilesScanned": source_files_scanned,
        "reportsScanned": len(json_reports),
        "auditReportsScanned": audit_reports_scanned,
        "sessionReportsScanned": session_reports_scanned,
        "forbiddenHitCount": forbidden_hit_count,
        "redactionPass": forbidden_hit_count == 0,
        "files": [
            {"path": repo_relative(path), "sizeBytes": path.stat().st_size}
            for path in files
        ],
        "reports": json_reports,
    }


def write_manifest_markdown(path: Path, manifest: dict[str, Any]) -> None:
    lines = [
        "# Vision Agent Quality Artifact Manifest",
        "",
        f"- Generated UTC: `{manifest['generatedAtUtc']}`",
        f"- Artifact: `{manifest['artifactName']}`",
        f"- Scan policy: `{manifest['scanPolicyVersion']}`",
        f"- Source files scanned: `{manifest['sourceFilesScanned']}`",
        f"- Reports scanned: `{manifest['reportsScanned']}`",
        f"- Audit reports scanned: `{manifest['auditReportsScanned']}`",
        f"- Session reports scanned: `{manifest['sessionReportsScanned']}`",
        f"- Forbidden hits: `{manifest['forbiddenHitCount']}`",
        f"- Redaction pass: `{manifest['redactionPass']}`",
        "",
        "| File | Size bytes |",
        "| --- | ---: |",
    ]
    for item in manifest["files"]:
        lines.append(f"| {item['path']} | {item['sizeBytes']} |")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--require-non-local-workflow-run",
        action="store_true",
        help="Fail if any JSON report workflowRun field is 'local'. Use this in GitHub Actions.",
    )
    parser.add_argument("--write-manifest", help="Optional JSON artifact manifest output path.")
    parser.add_argument("--write-report", help="Optional Markdown artifact manifest output path.")
    parser.add_argument(
        "--scan-source-files",
        action="store_true",
        help="Scan repository json/md/txt/trx/js/cs/ps1/py/yml files for real credential and CPA BaseUrl leaks.",
    )
    parser.add_argument(
        "--forbidden-fragment",
        action="append",
        default=[],
        help="Literal secret fragment that must not appear in reports or scanned source files. May be repeated.",
    )
    args = parser.parse_args()

    errors: list[str] = []
    configured_fragments = [item for item in args.forbidden_fragment if item]
    env_value = os.environ.get("CV_AGENT_FORBIDDEN_SECRET_FRAGMENTS", "")
    configured_fragments.extend(item.strip() for item in re.split(r"[\r\n;]+", env_value) if item.strip())
    expected_files = JSON_REPORTS + MARKDOWN_REPORTS + TEXT_OUTPUTS
    expected_files.extend(collect_optional_agent_run_audit_files())
    trx_files = sorted(TEST_RESULTS_DIR.glob("*.trx"))
    if not trx_files:
        errors.append(f"{repo_relative(TEST_RESULTS_DIR)} must contain at least one TRX file.")
    expected_files.extend(trx_files)

    for path in expected_files:
        if not path.exists():
            errors.append(f"Missing artifact file: {repo_relative(path)}")
            continue
        if path.suffix.lower() in {".json", ".jsonl", ".md", ".txt", ".trx"}:
            text = path.read_text(encoding="utf-8", errors="ignore")
            validate_no_secret_leaks(path, text, errors)
            validate_forbidden_fragments(path, text, configured_fragments, errors)
            validate_agent_run_jsonl(path, text, errors)

    report_summaries: list[dict[str, Any]] = []
    for path in JSON_REPORTS:
        if not path.exists():
            continue
        report = load_json(path)
        workflow_run = validate_workflow_run(path, report, args.require_non_local_workflow_run, errors)
        if path.name in {"real_llm_planner_shadow_eval.json", "real_llm_planner_shadow_eval.holdout.json"}:
            validate_shadow_report(path, report, errors)
        validate_runtime_preview_case_counts(path, report, errors)
        validate_runtime_preview_final_contract(path, report, errors)
        report_summaries.append(
            {
                "path": repo_relative(path),
                "id": report.get("benchmarkId") or report.get("evalId"),
                "workflowRun": workflow_run,
                "summary": report.get("summary", {}),
            }
        )

    source_files_scanned = 0
    if args.scan_source_files:
        source_files = iter_source_scan_files()
        source_files_scanned = len(source_files)
        validate_source_secret_scan(source_files, configured_fragments, errors)

    if errors:
        for error in errors:
            print(f"error: {error}")
        return 2

    if args.write_manifest:
        manifest_path = Path(args.write_manifest)
        if not manifest_path.is_absolute():
            manifest_path = REPO_ROOT / manifest_path
        manifest_path.parent.mkdir(parents=True, exist_ok=True)
        audit_reports_scanned = len([
            path for path in expected_files
            if "audit" in path.name.lower()
        ])
        session_reports_scanned = len([
            path for path in expected_files
            if "session" in path.name.lower() or "runtime_preview" in path.name.lower()
        ])
        manifest = build_manifest(
            report_summaries,
            expected_files,
            source_files_scanned,
            audit_reports_scanned,
            session_reports_scanned,
            forbidden_hit_count=0,
        )
        manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        if args.write_report:
            report_path = Path(args.write_report)
            if not report_path.is_absolute():
                report_path = REPO_ROOT / report_path
            report_path.parent.mkdir(parents=True, exist_ok=True)
            write_manifest_markdown(report_path, manifest)

    print(
        "validated Vision Agent artifacts "
        f"files={len(expected_files)} reports={len(report_summaries)} sourceFilesScanned={source_files_scanned}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
