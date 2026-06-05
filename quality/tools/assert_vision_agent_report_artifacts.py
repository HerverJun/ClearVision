#!/usr/bin/env python3
"""Validate Vision Agent quality report artifacts before upload."""

from __future__ import annotations

import argparse
import json
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
    args = parser.parse_args()

    errors: list[str] = []
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
            validate_no_secret_leaks(path, path.read_text(encoding="utf-8", errors="ignore"), errors)

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

    print(f"validated Vision Agent artifacts files={len(expected_files)} reports={len(report_summaries)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
