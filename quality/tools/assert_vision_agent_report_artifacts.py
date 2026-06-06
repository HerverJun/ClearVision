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

JSON_REPORTS = [
    REPORTS_DIR / "VisionAgent_business_benchmark_baseline.json",
    REPORTS_DIR / "planner_autonomy_benchmark.json",
    REPORTS_DIR / "real_llm_planner_shadow_eval.json",
]

MARKDOWN_REPORTS = [
    REPORTS_DIR / "VisionAgent_business_benchmark_baseline.md",
    REPORTS_DIR / "planner_autonomy_benchmark.md",
    REPORTS_DIR / "real_llm_planner_shadow_eval.md",
]

TEXT_OUTPUTS = [
    TEST_RESULTS_DIR / "agent_ui_contract_output.txt",
]

SOURCE_SCAN_EXTENSIONS = {
    ".cs",
    ".js",
    ".json",
    ".md",
    ".mjs",
    ".ps1",
    ".py",
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
    ]
]

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


def validate_no_secret_leaks(path: Path, text: str, errors: list[str]) -> None:
    for pattern in FORBIDDEN_SECRET_PATTERNS:
        if pattern.search(text):
            errors.append(f"{repo_relative(path)} leaks forbidden secret/auth fragment: {pattern.pattern}")


def validate_forbidden_fragments(path: Path, text: str, fragments: list[str], errors: list[str]) -> None:
    for fragment in fragments:
        if fragment and fragment in text:
            errors.append(f"{repo_relative(path)} leaks configured forbidden fragment.")


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


def build_manifest(json_reports: list[dict[str, Any]], files: list[Path]) -> dict[str, Any]:
    return {
        "schemaVersion": "2026-06-05.vision-agent-quality-artifact-manifest.v1",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "artifactName": "vision-agent-quality-suite",
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
    trx_files = sorted(TEST_RESULTS_DIR.glob("*.trx"))
    if not trx_files:
        errors.append(f"{repo_relative(TEST_RESULTS_DIR)} must contain at least one TRX file.")
    expected_files.extend(trx_files)

    for path in expected_files:
        if not path.exists():
            errors.append(f"Missing artifact file: {repo_relative(path)}")
            continue
        if path.suffix.lower() in {".json", ".md", ".txt", ".trx"}:
            text = path.read_text(encoding="utf-8", errors="ignore")
            validate_no_secret_leaks(path, text, errors)
            validate_forbidden_fragments(path, text, configured_fragments, errors)

    report_summaries: list[dict[str, Any]] = []
    for path in JSON_REPORTS:
        if not path.exists():
            continue
        report = load_json(path)
        workflow_run = validate_workflow_run(path, report, args.require_non_local_workflow_run, errors)
        if path.name == "real_llm_planner_shadow_eval.json":
            validate_shadow_report(path, report, errors)
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
        manifest = build_manifest(report_summaries, expected_files)
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
