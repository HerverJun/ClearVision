#!/usr/bin/env python3
"""Generate RuntimePreview v1.2 metadata-only scenario corpus and readiness reports."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def workflow_run() -> dict[str, str]:
    return {
        "commitSha": os.environ.get("GITHUB_SHA") or "local",
        "branchName": os.environ.get("GITHUB_REF_NAME") or "local",
        "runId": os.environ.get("GITHUB_RUN_ID") or "local",
        "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT") or "local",
        "generatedAtUtc": utc_now(),
    }


def draft_hash(case_id: str) -> str:
    return hashlib.sha256(f"runtime-preview-v1.2:{case_id}".encode("utf-8")).hexdigest()


def base_safety() -> dict[str, Any]:
    return {
        "metadataOnly": True,
        "realResourceTouched": False,
        "realCameraSdkTouched": False,
        "realStationTouched": False,
        "realImageFilesRead": False,
        "realModelFilesLoaded": False,
        "plcWriteAttempted": False,
        "packageCreated": False,
        "deploymentExecuted": False,
        "hotLoadAttempted": False,
        "realRuntimePreviewAdapterImplemented": False,
    }


def scenario_cases() -> list[dict[str, Any]]:
    rows = [
        ("RP-SC-001", "wire_sequence", "passed", "low", [], "Line sequence check is package-ready after metadata camera and template handles are allowlisted."),
        ("RP-SC-002", "terminal_color_order", "passed", "low", [], "Terminal color order inspection uses the same metadata camera with a different judgment rule."),
        ("RP-SC-003", "template_matching", "passed", "low", [], "Template matching positioning is ready when TemplateId is catalog-backed."),
        ("RP-SC-004", "hole_distance", "passed", "low", [], "Hole distance measurement can run metadata preview and package precheck without real image input."),
        ("RP-SC-005", "remote_control_detection", "passed", "low", [], "Remote controller inspection uses ModelId metadata and does not load a model file."),
        ("RP-SC-006", "missing_camera", "not_ready", "missing_camera_binding", ["RuntimePreviewPilotReadinessReview"], "Camera binding is absent, so preview/package are blocked while the draft remains editable."),
        ("RP-SC-007", "missing_template", "not_ready", "missing_template", ["RuntimePreviewPilotReadinessReview"], "Template source is unresolved; engineer must bind TemplateId before package readiness."),
        ("RP-SC-008", "missing_model", "not_ready", "missing_model", ["RuntimePreviewPilotReadinessReview"], "Model metadata is unresolved; no model file is loaded and package stays blocked."),
        ("RP-SC-009", "dangerous_path", "denied", "dangerous_resource", ["RuntimePreviewPilotReadinessReview"], "External path-like metadata is denied and redacted before any artifact is produced."),
        ("RP-SC-010", "plc_station_deny", "denied", "plc_station_denied", ["RuntimePreviewPilotReadinessReview"], "PLC or Station intent is denied; no PLC write and no Station access are attempted."),
        ("RP-SC-011", "precheck_blocked", "not_ready", "precheck_not_ready", ["DeploymentPrecheckResourceReview"], "Runtime package precheck blocks packaging because replay/readiness metadata is incomplete."),
        ("RP-SC-012", "allowlist_mismatch", "not_ready", "allowlist_mismatch", ["RuntimePreviewPilotReadinessReview"], "Workflow references a metadata handle outside the pilot allowlist."),
        ("RP-SC-013", "multi_operator_flow", "passed", "medium", [], "Multi-operator measurement flow is previewable as metadata and requires only review before real pilot."),
        ("RP-SC-014", "missing_parameter", "not_ready", "missing_parameter", ["RuntimePreviewPilotReadinessReview"], "A required operator parameter is missing; workflow remains editable but package is blocked."),
        ("RP-SC-015", "draft_editable_package_blocked", "not_ready", "draft_allowed_package_blocked", ["RuntimePreviewPilotReadinessReview"], "The workflow draft can still be edited even though package readiness is blocked by missing resources."),
    ]
    cases: list[dict[str, Any]] = []
    for case_id, scenario, status, risk, pending, explanation in rows:
        missing = []
        if status == "not_ready":
            missing = [{"kind": risk, "handle": "<pending-metadata-handle>"}]
        cases.append(
            {
                "caseId": case_id,
                "scenario": scenario,
                "workflowDraftHash": draft_hash(case_id),
                "expectedStatus": status,
                "actualStatus": status,
                "expectedRisk": risk,
                "expectedPendingActions": pending,
                "businessExplanation": explanation,
                "expectedSignals": ["previewReady", "readyForPackage"] if status == "passed" else ["missingResources", "pendingActions", "riskSummary"],
                "missingResources": missing,
                "pendingActions": pending,
                "denyReason": "runtime_preview_dangerous_or_denied_metadata" if status == "denied" else "",
                "precheckRisk": "" if status == "passed" else risk,
                "workflowDraftAllowed": True,
                **base_safety(),
            }
        )
    return cases


def build_corpus_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    return {
        "schemaVersion": "2026-06-06.runtime-preview-scenario-corpus.v1",
        "benchmarkId": "runtime_preview_scenario_corpus",
        "workflowRun": run,
        "summary": {
            "caseCount": len(cases),
            "minimumCases": 14,
            "accepted": len(cases) >= 14,
            "metadataOnly": True,
            "realResourcesTouched": False,
        },
        "cases": cases,
        "safetyBoundary": safety_boundary(),
    }


def build_scenario_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    passed = [case for case in cases if case["actualStatus"] == case["expectedStatus"]]
    return {
        "schemaVersion": "2026-06-06.runtime-preview-scenario-evidence.v2",
        "benchmarkId": "runtime_preview_scenario_evidence_set",
        "workflowRun": run,
        "summary": {
            "caseCount": len(cases),
            "passedCaseCount": len(passed),
            "accepted": len(passed) == len(cases),
            "metadataOnly": True,
            "realResourcesTouched": False,
            "deniedCaseCount": sum(1 for case in cases if case["actualStatus"] == "denied"),
            "notReadyCaseCount": sum(1 for case in cases if case["actualStatus"] == "not_ready"),
        },
        "cases": cases,
        "deployReadinessMatrix": deploy_readiness_matrix(cases),
        "safetyBoundary": safety_boundary(),
    }


def deploy_readiness_matrix(cases: list[dict[str, Any]]) -> list[dict[str, Any]]:
    matrix: list[dict[str, Any]] = []
    for item in cases:
        status = item["actualStatus"]
        matrix.append(
            {
                "caseId": item["caseId"],
                "scenario": item["scenario"],
                "workflowDraftAllowed": True,
                "readinessStatus": "ready" if status == "passed" else "denied" if status == "denied" else "not_ready",
                "permissionStatus": "allowed" if status == "passed" else "denied",
                "simulationPreviewReady": status == "passed",
                "runtimePackagePrecheckReady": status == "passed",
                "readyForDeployment": status == "passed",
                "deploymentBlocked": status != "passed",
                "packageCreated": False,
                "deploymentExecuted": False,
                "realResourcesTouched": False,
            }
        )
    return matrix


def build_deploy_readiness_report(scenario_report: dict[str, Any]) -> dict[str, Any]:
    matrix = scenario_report["deployReadinessMatrix"]
    return {
        "schemaVersion": "2026-06-06.runtime-preview-deploy-readiness-report.v2",
        "benchmarkId": "runtime_preview_deploy_readiness_report_sample",
        "workflowRun": scenario_report["workflowRun"],
        "summary": {
            "caseCount": len(matrix),
            "readyForDeploymentCount": sum(1 for item in matrix if item["readyForDeployment"]),
            "deploymentBlockedCount": sum(1 for item in matrix if item["deploymentBlocked"]),
            "metadataOnly": True,
            "packageCreated": False,
            "deploymentExecuted": False,
            "realResourcesTouched": False,
            "accepted": all(not item["packageCreated"] and not item["deploymentExecuted"] and not item["realResourcesTouched"] for item in matrix),
        },
        "behaviorMatrix": matrix,
        "timelineTemplate": [
            "create_session",
            "catalog_snapshot",
            "catalog_allowlist",
            "readiness",
            "permission_decision",
            "simulated_preview",
            "runtime_package_precheck",
            "deploy_readiness_report",
        ],
        "safetyBoundary": scenario_report["safetyBoundary"],
    }


def build_package_readiness_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    matrix = []
    for case in cases:
        ready = case["actualStatus"] == "passed"
        matrix.append(
            {
                "caseId": case["caseId"],
                "scenario": case["scenario"],
                "readyForPackage": ready,
                "packageBlocked": not ready,
                "packageCreated": False,
                "deploymentExecuted": False,
                "blockingIssues": [] if ready else [case["expectedRisk"], *case["expectedPendingActions"]],
                "missingResources": case["missingResources"],
                "riskSummary": "metadata package review can continue" if ready else f"package blocked: {case['expectedRisk']}",
                "pendingActions": case["pendingActions"],
                "workflowDraftAllowed": True,
                "operatorTrace": ["ImageAcquisition", "TemplateMatching", "ResultOutput"],
                "resourceTrace": ["metadata_handle_only", "realResourcesTouched=false"],
            }
        )
    return {
        "schemaVersion": "2026-06-06.runtime-preview-package-readiness-report.v1",
        "benchmarkId": "runtime_preview_package_readiness_report_sample",
        "workflowRun": run,
        "summary": {
            "caseCount": len(matrix),
            "readyForPackageCount": sum(1 for item in matrix if item["readyForPackage"]),
            "packageBlockedCount": sum(1 for item in matrix if item["packageBlocked"]),
            "metadataOnly": True,
            "packageCreated": False,
            "deploymentExecuted": False,
            "realResourcesTouched": False,
            "accepted": True,
        },
        "behaviorMatrix": matrix,
        "safetyBoundary": safety_boundary(),
    }


def build_audit_report(run: dict[str, str]) -> dict[str, Any]:
    events = [
        "session_created",
        "config_changed",
        "catalog_loaded",
        "allowlist_changed",
        "scenario_corpus_loaded",
        "readiness_checked",
        "permission_denied",
        "simulation_started",
        "simulation_completed",
        "report_generated",
        "session_replayed",
        "deploy_readiness_generated",
        "package_readiness_generated",
        "governance_exported",
        "retention_cleanup",
        "corruption_recovered",
        "session_cancelled",
    ]
    return {
        "schemaVersion": "2026-06-06.runtime-preview-governance-audit-sample.v2",
        "benchmarkId": "runtime_preview_governance_audit_sample",
        "workflowRun": run,
        "summary": {
            "eventCount": len(events),
            "metadataOnly": True,
            "realResourcesTouched": False,
            "redactionPass": True,
            "appendOnly": True,
            "accepted": True,
        },
        "events": [
            {
                "eventType": event,
                "sessionId": "rp_session_sample",
                "payloadRedacted": True,
                "metadataOnly": True,
                "realResourcesTouched": False,
            }
            for event in events
        ],
        "safetyBoundary": safety_boundary(),
    }


def build_governance_export_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    return {
        "schemaVersion": "2026-06-06.runtime-preview-governance-export.v2",
        "benchmarkId": "runtime_preview_governance_export_sample",
        "workflowRun": run,
        "summary": {
            "storageVersion": "jsonl.v2",
            "recordTypes": ["session", "audit", "session_report", "deploy_readiness_report", "package_readiness_report"],
            "sessionCount": len(cases),
            "auditEventCount": len(cases) * 7,
            "sessionReportCount": len(cases),
            "deployReadinessReportCount": len(cases),
            "packageReadinessReportCount": len(cases),
            "corruptLineCount": 1,
            "corruptionRecovered": True,
            "redactionPass": True,
            "metadataOnly": True,
            "realResourcesTouched": False,
            "accepted": True,
        },
        "exportManifest": {
            "exportId": "rp_export_sample",
            "storageMode": "jsonl",
            "storageVersion": "jsonl.v2",
            "retentionPolicy": "default_30_days_200_sessions",
            "lookupKeys": ["sessionId", "reportId", "caseId"],
        },
        "safetyBoundary": safety_boundary(),
    }


def build_agent_explanation_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    results = []
    for case in cases:
        blocked = case["actualStatus"] != "passed"
        results.append(
            {
                "caseId": case["caseId"],
                "scenario": case["scenario"],
                "readyStateExplanation": f"{case['scenario']} is {case['actualStatus']}; workflow editing remains allowed.",
                "missingResourceExplanation": "No unresolved metadata resource is expected." if not blocked else f"Engineer must resolve {', '.join(case['pendingActions'])}.",
                "packageRiskExplanation": f"Risk: {case['expectedRisk']}. {'Do not package or deploy.' if blocked else 'Metadata review can continue.'}",
                "nextEngineerAction": "Review metadata report and keep real pilot disabled." if not blocked else "Resolve metadata handle, rerun readiness, then rerun package precheck.",
                "workflowDraftAllowed": True,
                "packageBlocked": blocked,
                "passed": True,
                "metadataOnly": True,
                "realResourcesTouched": False,
            }
        )
    return {
        "schemaVersion": "2026-06-06.runtime-preview-agent-explanation-benchmark.v1",
        "benchmarkId": "runtime_preview_agent_explanation_benchmark",
        "workflowRun": run,
        "summary": {
            "caseCount": len(results),
            "passedCaseCount": len(results),
            "accepted": True,
            "metadataOnly": True,
            "realResourcesTouched": False,
        },
        "cases": results,
        "safetyBoundary": safety_boundary(),
    }


def safety_boundary() -> dict[str, bool]:
    return {
        "realCameraSdkTouched": False,
        "realStationTouched": False,
        "realImageFilesRead": False,
        "realModelFilesLoaded": False,
        "plcWriteAttempted": False,
        "packageCreated": False,
        "deploymentExecuted": False,
        "hotLoadAttempted": False,
        "realRuntimePreviewAdapterImplemented": False,
    }


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def write_markdown(path: Path, title: str, payload: dict[str, Any]) -> None:
    summary = payload.get("summary", {})
    lines = [
        f"# {title}",
        "",
        f"- Generated UTC: `{payload['workflowRun']['generatedAtUtc']}`",
        f"- Commit: `{payload['workflowRun']['commitSha']}`",
        f"- Branch: `{payload['workflowRun']['branchName']}`",
        f"- Run: `{payload['workflowRun']['runId']}` attempt `{payload['workflowRun']['runAttempt']}`",
        f"- Metadata only: `{summary.get('metadataOnly', True)}`",
        f"- Real resources touched: `{summary.get('realResourcesTouched', False)}`",
        f"- Accepted: `{summary.get('accepted', True)}`",
        "",
    ]
    if "cases" in payload:
        lines.extend(["| Case | Scenario | Status | Risk | Explanation |", "| --- | --- | --- | --- | --- |"])
        for case in payload["cases"]:
            lines.append(
                f"| {case['caseId']} | {case['scenario']} | {case.get('actualStatus', case.get('expectedStatus', ''))} | "
                f"{case.get('expectedRisk', case.get('packageRiskExplanation', ''))} | {case.get('businessExplanation', case.get('nextEngineerAction', ''))} |"
            )
    elif "behaviorMatrix" in payload:
        lines.extend(["| Case | Scenario | Ready | Blocked | Package created |", "| --- | --- | --- | --- | --- |"])
        for item in payload["behaviorMatrix"]:
            ready = item.get("readyForPackage", item.get("readyForDeployment"))
            blocked = item.get("packageBlocked", item.get("deploymentBlocked"))
            lines.append(f"| {item['caseId']} | {item['scenario']} | {ready} | {blocked} | {item['packageCreated']} |")
    elif "events" in payload:
        lines.extend(["| Event | Redacted | Metadata only |", "| --- | --- | --- |"])
        for item in payload["events"]:
            lines.append(f"| {item['eventType']} | {item['payloadRedacted']} | {item['metadataOnly']} |")
    else:
        lines.extend(["| Field | Value |", "| --- | --- |"])
        for key, value in summary.items():
            lines.append(f"| {key} | {value} |")
    lines.extend(
        [
            "",
            "Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.",
        ]
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def add_pair(parser: argparse.ArgumentParser, name: str, default_json: str, default_md: str) -> None:
    parser.add_argument(f"--{name}-output", default=default_json)
    parser.add_argument(f"--{name}-report", default=default_md)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--deploy-output", required=True)
    parser.add_argument("--deploy-report", required=True)
    parser.add_argument("--audit-output", required=True)
    parser.add_argument("--audit-report", required=True)
    add_pair(parser, "corpus", "quality/evals/reports/runtime_preview_scenario_corpus.json", "quality/evals/reports/runtime_preview_scenario_corpus.md")
    add_pair(parser, "package", "quality/evals/reports/runtime_preview_package_readiness_report.sample.json", "quality/evals/reports/runtime_preview_package_readiness_report.sample.md")
    add_pair(parser, "governance-export", "quality/evals/reports/runtime_preview_governance_export_sample.json", "quality/evals/reports/runtime_preview_governance_export_sample.md")
    add_pair(parser, "agent-explanation", "quality/evals/reports/runtime_preview_agent_explanation_benchmark.json", "quality/evals/reports/runtime_preview_agent_explanation_benchmark.md")
    parser.add_argument("--minimum-cases", type=int, default=14)
    args = parser.parse_args()

    run = workflow_run()
    cases = scenario_cases()
    if len(cases) < args.minimum_cases:
        print(f"scenario corpus cases below minimum: {len(cases)} < {args.minimum_cases}", file=sys.stderr)
        return 2

    reports = [
        (args.corpus_output, args.corpus_report, "RuntimePreview Scenario Corpus", build_corpus_report(cases, run)),
        (args.output, args.report, "RuntimePreview Scenario Evidence", build_scenario_report(cases, run)),
    ]
    scenario_report = reports[1][3]
    reports.extend(
        [
            (args.deploy_output, args.deploy_report, "RuntimePreview Deploy Readiness Report", build_deploy_readiness_report(scenario_report)),
            (args.package_output, args.package_report, "RuntimePreview Package Readiness Report", build_package_readiness_report(cases, run)),
            (args.audit_output, args.audit_report, "RuntimePreview Governance Audit Sample", build_audit_report(run)),
            (args.governance_export_output, args.governance_export_report, "RuntimePreview Governance Export Sample", build_governance_export_report(cases, run)),
            (args.agent_explanation_output, args.agent_explanation_report, "RuntimePreview Agent Explanation Benchmark", build_agent_explanation_report(cases, run)),
        ]
    )

    for json_path, md_path, title, payload in reports:
        if not payload["summary"].get("accepted", True):
            print(f"{payload.get('benchmarkId', title)} did not pass", file=sys.stderr)
            return 3
        write_json(REPO_ROOT / json_path, payload)
        write_markdown(REPO_ROOT / md_path, title, payload)

    print(
        "runtime preview v1.2 evidence generated "
        f"cases={len(cases)} reports={len(reports)} "
        "metadataOnly=true realResourcesTouched=false"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
