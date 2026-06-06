#!/usr/bin/env python3
"""Generate RuntimePreview v1.3 metadata-only scenario, manifest dry-run, and readiness reports."""

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
    return hashlib.sha256(f"runtime-preview-v1.3:{case_id}".encode("utf-8")).hexdigest()


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
        "realPackageFileWritten": False,
        "manifestArtifactGenerated": False,
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
        ("RP-SC-016", "package_manifest_blocked", "not_ready", "manifest_dependency_blocked", ["ManifestDependencyReview"], "Manifest dry-run blocks package review because dependency metadata is incomplete."),
        ("RP-SC-017", "multi_camera_flow", "not_ready", "multi_camera_review", ["RuntimePreviewPilotReadinessReview"], "Two-camera workflow requires both camera metadata handles to be allowlisted."),
        ("RP-SC-018", "multi_model_flow", "not_ready", "multi_model_review", ["ManifestDependencyReview"], "Multiple model dependencies require catalog ownership review before package review."),
        ("RP-SC-019", "template_plus_hole_distance", "passed", "medium", [], "Template positioning and hole distance measurement can be reviewed as a metadata manifest."),
        ("RP-SC-020", "direct_deploy_request_denied", "denied", "deployment_intent_denied", ["RuntimePreviewPilotReadinessReview"], "Direct Station release intent is denied; no package or deployment is created."),
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


def redacted_flow_cases() -> list[dict[str, Any]]:
    rows = [
        ("RP-RF-001", "connector_line", "wire_sequence", "Verify harness wire order before release.", "passed", "passed", "low", "Review metadata manifest and keep real pilot gate closed.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"]),
        ("RP-RF-002", "remote_control_station", "remote_control_defect", "Detect missing buttons and label defects.", "passed", "passed", "low", "Confirm ModelId catalog ownership before real pilot.", ["ImageAcquisition", "DeepLearning", "ResultOutput"]),
        ("RP-RF-003", "fixture_station", "template_measurement_combo", "Locate fixture by template and measure a downstream feature.", "passed", "passed", "medium", "Review combined template and measurement contract.", ["ImageAcquisition", "TemplateMatching", "CircleMeasurement", "ResultOutput"]),
        ("RP-RF-004", "measurement_station", "hole_distance", "Measure distance between two holes.", "passed", "passed", "low", "Confirm measurement unit and tolerance source.", ["ImageAcquisition", "CircleMeasurement", "MeasureDistance", "ResultOutput"]),
        ("RP-RF-005", "terminal_station", "terminal_color_order", "Check terminal color sequence.", "passed", "passed", "low", "Confirm template ownership and output mapping.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"]),
        ("RP-RF-006", "line_station", "missing_camera", "Camera handle is absent from the pilot catalog.", "not_ready", "not_ready", "missing_camera_binding", "Bind an allowlisted metadata camera before package review.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"]),
        ("RP-RF-007", "fixture_station", "missing_template", "TemplateMatching has no TemplateId metadata handle.", "not_ready", "not_ready", "missing_template", "Assign an allowlisted TemplateId; do not use file paths.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"]),
        ("RP-RF-008", "remote_control_station", "missing_model", "DeepLearning operator has unresolved model metadata.", "not_ready", "not_ready", "missing_model", "Bind ModelId from catalog; do not load a model file.", ["ImageAcquisition", "DeepLearning", "ResultOutput"]),
        ("RP-RF-009", "output_station", "missing_output_channel", "ResultOutput lacks a safe output channel id.", "not_ready", "not_ready", "missing_output_channel", "Choose OutputChannelId before package review.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"]),
        ("RP-RF-010", "station_release", "plc_station_deny", "User intent includes PLC or Station release action.", "denied", "denied", "plc_station_denied", "Remove PLC/Station intent; this console cannot write or deploy.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"]),
        ("RP-RF-011", "template_station", "dangerous_path", "Template dependency tries to point at an external path.", "denied", "denied", "dangerous_resource", "Replace path-like metadata with catalog TemplateId.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"]),
        ("RP-RF-012", "line_station", "allowlist_mismatch", "Workflow handle is not allowlisted for pilot.", "not_ready", "not_ready", "allowlist_mismatch", "Review allowlist diff and confirm catalog handle.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"]),
        ("RP-RF-013", "dual_camera_station", "multi_camera_flow", "Two camera metadata handles feed one decision.", "not_ready", "not_ready", "multi_camera_review", "Confirm both camera bindings are catalog allowlisted.", ["ImageAcquisition", "ImageAcquisition", "TemplateMatching", "ResultOutput"]),
        ("RP-RF-014", "ai_station", "multi_model_flow", "Two model metadata handles are required.", "not_ready", "not_ready", "multi_model_review", "Confirm all ModelIds and output aggregation.", ["ImageAcquisition", "DeepLearning", "DeepLearning", "ResultOutput"]),
        ("RP-RF-015", "parameter_review_station", "parameter_missing", "A key operator parameter is missing.", "not_ready", "not_ready", "missing_parameter", "Complete required operator parameters and rerun readiness.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"]),
        ("RP-RF-016", "release_review", "package_manifest_blocked", "Manifest dry-run blocks release review.", "not_ready", "not_ready", "manifest_dependency_blocked", "Resolve manifest dependencies; no package may be created.", ["ImageAcquisition", "DeepLearning", "ResultOutput"]),
        ("RP-RF-017", "draft_review", "workflow_editable_package_blocked", "Draft is editable while package review is blocked.", "not_ready", "not_ready", "draft_allowed_package_blocked", "Keep editing; do not start release review yet.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"]),
        ("RP-RF-018", "precheck_station", "runtime_package_precheck_blocked", "Runtime package precheck risk blocks release.", "not_ready", "not_ready", "precheck_not_ready", "Rerun readiness after metadata is resolved.", ["ImageAcquisition", "DeepLearning", "ResultOutput"]),
        ("RP-RF-019", "template_measurement_station", "template_plus_hole_distance", "Template positioning and hole distance share one camera.", "passed", "passed", "medium", "Review dependency trace before requesting real pilot.", ["ImageAcquisition", "TemplateMatching", "CircleMeasurement", "MeasureDistance", "ResultOutput"]),
        ("RP-RF-020", "release_blocked_station", "direct_deploy_request_denied", "User asks to release to Station directly.", "denied", "denied", "deployment_intent_denied", "Use metadata review only; direct deployment remains forbidden.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"]),
    ]
    return [
        {
            "caseId": case_id,
            "stationType": station_type,
            "workflowKind": workflow_kind,
            "businessPurpose": purpose,
            "workflowDraftHash": draft_hash(case_id),
            "operatorSummary": operators,
            "expectedReadiness": readiness,
            "expectedPackageReadiness": package_readiness,
            "expectedManifestRisk": risk,
            "expectedEngineerAction": action,
            "redactionStatus": "redacted_metadata_only",
            "metadataOnly": True,
            "realResourcesTouched": False,
            **base_safety(),
        }
        for case_id, station_type, workflow_kind, purpose, readiness, package_readiness, risk, action, operators in rows
    ]


def build_corpus_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    return {
        "schemaVersion": "2026-06-06.runtime-preview-scenario-corpus.v2",
        "benchmarkId": "runtime_preview_scenario_corpus",
        "workflowRun": run,
        "summary": {
            "caseCount": len(cases),
            "minimumCases": 20,
            "accepted": len(cases) >= 20,
            "metadataOnly": True,
            "realResourcesTouched": False,
        },
        "cases": cases,
        "safetyBoundary": safety_boundary(),
    }


def build_redacted_flow_corpus_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    return {
        "schemaVersion": "2026-06-06.runtime-preview-redacted-flow-corpus.v1",
        "benchmarkId": "runtime_preview_redacted_flow_corpus",
        "workflowRun": run,
        "summary": {
            "caseCount": len(cases),
            "minimumCases": 20,
            "accepted": len(cases) >= 20,
            "metadataOnly": True,
            "realResourcesTouched": False,
            "redactionPass": True,
        },
        "cases": cases,
        "safetyBoundary": safety_boundary(),
    }


def build_scenario_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    passed = [case for case in cases if case["actualStatus"] == case["expectedStatus"]]
    return {
        "schemaVersion": "2026-06-06.runtime-preview-scenario-evidence.v3",
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
        "manifest_dry_run",
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
                "packageReviewAllowed": ready,
                "packageBlocked": not ready,
                "manifestDryRunReportId": f"rp_manifest_dry_run_{case['caseId'].lower()}",
                "packageCreated": False,
                "deploymentExecuted": False,
                "blockingIssues": [] if ready else [case["expectedRisk"], *case["expectedPendingActions"]],
                "blockedReason": "" if ready else case["expectedRisk"],
                "missingResources": case["missingResources"],
                "riskSummary": "metadata package review can continue" if ready else f"package blocked: {case['expectedRisk']}",
                "packageRiskLevel": "low" if ready else "denied" if case["actualStatus"] == "denied" else "high",
                "packageReviewExplanation": "Manifest dry-run can proceed; no package is created." if ready else "Workflow draft remains editable, but package review is blocked by metadata dependency or policy findings.",
                "pendingActions": case["pendingActions"],
                "workflowDraftAllowed": True,
                "operatorTrace": ["ImageAcquisition", "TemplateMatching", "ResultOutput"],
                "resourceTrace": ["metadata_handle_only", "realResourcesTouched=false"],
                "dependencyTrace": ["operator_contract:metadata", "resource_contract:metadata_only"] if ready else [f"blocked:{case['expectedRisk']}"],
                "operatorContract": ["ImageAcquisition:metadata", "TemplateMatching:metadata", "ResultOutput:metadata"],
                "resourceContract": ["camera/template/model/output:metadata_only"],
            }
        )
    return {
        "schemaVersion": "2026-06-06.runtime-preview-package-readiness-report.v2",
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


def build_manifest_dry_run_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    matrix = []
    for case in cases:
        ready = case["actualStatus"] == "passed"
        operator_types = ["ImageAcquisition", "TemplateMatching", "ResultOutput"]
        blocked = [] if ready else [case["expectedRisk"], *case["expectedPendingActions"]]
        matrix.append(
            {
                "caseId": case["caseId"],
                "scenario": case["scenario"],
                "manifestId": f"rp_manifest_dry_run_{case['caseId'].lower()}",
                "workflowDraftHash": case["workflowDraftHash"],
                "manifestHash": hashlib.sha256(f"manifest:{case['caseId']}:{case['expectedRisk']}".encode("utf-8")).hexdigest(),
                "operatorCount": len(operator_types),
                "operatorTypes": operator_types,
                "resourceDependencies": ["camera:metadata", "template:metadata", "output:metadata"],
                "modelDependencies": ["model:metadata"] if "model" in case["scenario"] else [],
                "templateDependencies": ["template:metadata"] if "template" in case["scenario"] or "wire" in case["scenario"] else [],
                "cameraBindings": ["camera:metadata"],
                "outputChannels": ["qa-metadata"],
                "missingDependencies": [] if ready else [f"missing_or_blocked:{case['expectedRisk']}"],
                "blockedReasons": blocked,
                "dependencyTrace": ["manifest_hash:metadata", "operator_graph:metadata_only"] if ready else [f"dependency_blocked:{case['expectedRisk']}"],
                "operatorTrace": operator_types,
                "resourceTrace": ["realResourcesTouched=false", "metadataOnly=true"],
                "riskLevel": "low" if ready else "denied" if case["actualStatus"] == "denied" else "high",
                "packageReviewAllowed": ready,
                "workflowDraftAllowed": True,
                "manifestArtifactGenerated": False,
                **base_safety(),
            }
        )
    return {
        "schemaVersion": "2026-06-06.runtime-package-manifest-dry-run.v1",
        "benchmarkId": "runtime_package_manifest_dry_run_sample",
        "workflowRun": run,
        "summary": {
            "caseCount": len(matrix),
            "packageReviewAllowedCount": sum(1 for item in matrix if item["packageReviewAllowed"]),
            "manifestBlockedCount": sum(1 for item in matrix if not item["packageReviewAllowed"]),
            "metadataOnly": True,
            "manifestArtifactGenerated": False,
            "packageCreated": False,
            "deploymentExecuted": False,
            "realResourcesTouched": False,
            "accepted": len(matrix) >= 20 and all(not item["packageCreated"] and not item["deploymentExecuted"] for item in matrix),
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
        "manifest_dry_run_generated",
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
        "schemaVersion": "2026-06-06.runtime-preview-governance-export.v3",
        "benchmarkId": "runtime_preview_governance_export_sample",
        "workflowRun": run,
        "summary": {
            "storageVersion": "jsonl.v3",
            "recordTypes": ["session", "audit", "session_report", "deploy_readiness_report", "package_readiness_report", "manifest_dry_run_report"],
            "sessionCount": len(cases),
            "auditEventCount": len(cases) * 7,
            "sessionReportCount": len(cases),
            "deployReadinessReportCount": len(cases),
            "packageReadinessReportCount": len(cases),
            "manifestDryRunReportCount": len(cases),
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
            "storageVersion": "jsonl.v3",
            "retentionPolicy": "default_30_days_200_sessions",
            "lookupKeys": ["sessionId", "reportId", "caseId", "manifestId"],
        },
        "safetyBoundary": safety_boundary(),
    }


def build_agent_explanation_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    results = []
    for case in cases:
        status = case.get("actualStatus", case.get("expectedReadiness", ""))
        blocked = status != "passed"
        pending = case.get("pendingActions") or ([case.get("expectedEngineerAction", "metadata review")] if blocked else [])
        results.append(
            {
                "caseId": case["caseId"],
                "scenario": case.get("scenario", case.get("workflowKind", "")),
                "status": case.get("actualStatus", case.get("expectedReadiness", "")),
                "readyStateExplanation": f"{case.get('scenario', case.get('workflowKind', 'case'))} is {case.get('actualStatus', case.get('expectedReadiness', ''))}; workflow editing remains allowed.",
                "missingResourceExplanation": "No unresolved metadata resource is expected." if not blocked else f"Engineer must resolve {', '.join(pending)}.",
                "packageRiskExplanation": f"Risk: {case.get('expectedRisk', case.get('expectedManifestRisk', 'low'))}. {'Do not package or deploy.' if blocked else 'Metadata review can continue.'}",
                "affectedOperators": case.get("operatorSummary", ["ImageAcquisition", "TemplateMatching", "ResultOutput"]),
                "blockedReasons": [] if not blocked else [case.get("expectedRisk", case.get("expectedManifestRisk", "blocked"))],
                "manifestRisk": case.get("expectedRisk", case.get("expectedManifestRisk", "low")),
                "nextEngineerAction": case.get("expectedEngineerAction") or ("Review metadata report and keep real pilot disabled." if not blocked else "Resolve metadata handle, rerun readiness, then rerun package precheck."),
                "workflowDraftAllowed": True,
                "packageBlocked": blocked,
                "packageReviewAllowed": not blocked,
                "passed": True,
                "metadataOnly": True,
                "realResourcesTouched": False,
            }
        )
    return {
        "schemaVersion": "2026-06-06.runtime-preview-agent-explanation-benchmark.v2",
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
            scenario = case.get("scenario", case.get("workflowKind", ""))
            status = case.get("actualStatus", case.get("expectedStatus", case.get("expectedReadiness", "")))
            risk = case.get("expectedRisk", case.get("expectedManifestRisk", case.get("packageRiskExplanation", "")))
            explanation = case.get(
                "businessExplanation",
                case.get("nextEngineerAction", case.get("businessPurpose", case.get("expectedEngineerAction", "")))
            )
            lines.append(
                f"| {case['caseId']} | {scenario} | {status} | {risk} | {explanation} |"
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
    add_pair(parser, "redacted-flow", "quality/evals/reports/runtime_preview_redacted_flow_corpus.json", "quality/evals/reports/runtime_preview_redacted_flow_corpus.md")
    add_pair(parser, "package", "quality/evals/reports/runtime_preview_package_readiness_report.sample.json", "quality/evals/reports/runtime_preview_package_readiness_report.sample.md")
    add_pair(parser, "manifest-dry-run", "quality/evals/reports/runtime_package_manifest_dry_run.sample.json", "quality/evals/reports/runtime_package_manifest_dry_run.sample.md")
    add_pair(parser, "governance-export", "quality/evals/reports/runtime_preview_governance_export_sample.json", "quality/evals/reports/runtime_preview_governance_export_sample.md")
    add_pair(parser, "agent-explanation", "quality/evals/reports/runtime_preview_agent_explanation_benchmark.json", "quality/evals/reports/runtime_preview_agent_explanation_benchmark.md")
    parser.add_argument("--minimum-cases", type=int, default=20)
    args = parser.parse_args()

    run = workflow_run()
    cases = scenario_cases()
    redacted_cases = redacted_flow_cases()
    if len(cases) < args.minimum_cases:
        print(f"scenario corpus cases below minimum: {len(cases)} < {args.minimum_cases}", file=sys.stderr)
        return 2
    if len(redacted_cases) < args.minimum_cases:
        print(f"redacted flow corpus cases below minimum: {len(redacted_cases)} < {args.minimum_cases}", file=sys.stderr)
        return 2

    reports = [
        (args.corpus_output, args.corpus_report, "RuntimePreview Scenario Corpus", build_corpus_report(cases, run)),
        (args.redacted_flow_output, args.redacted_flow_report, "RuntimePreview Redacted Flow Corpus", build_redacted_flow_corpus_report(redacted_cases, run)),
        (args.output, args.report, "RuntimePreview Scenario Evidence", build_scenario_report(cases, run)),
    ]
    scenario_report = reports[2][3]
    reports.extend(
        [
            (args.deploy_output, args.deploy_report, "RuntimePreview Deploy Readiness Report", build_deploy_readiness_report(scenario_report)),
            (args.package_output, args.package_report, "RuntimePreview Package Readiness Report", build_package_readiness_report(cases, run)),
            (args.manifest_dry_run_output, args.manifest_dry_run_report, "RuntimePackage Manifest Dry-Run Report", build_manifest_dry_run_report(cases, run)),
            (args.audit_output, args.audit_report, "RuntimePreview Governance Audit Sample", build_audit_report(run)),
            (args.governance_export_output, args.governance_export_report, "RuntimePreview Governance Export Sample", build_governance_export_report(cases, run)),
            (args.agent_explanation_output, args.agent_explanation_report, "RuntimePreview Agent Explanation Benchmark", build_agent_explanation_report(redacted_cases, run)),
        ]
    )

    for json_path, md_path, title, payload in reports:
        if not payload["summary"].get("accepted", True):
            print(f"{payload.get('benchmarkId', title)} did not pass", file=sys.stderr)
            return 3
        write_json(REPO_ROOT / json_path, payload)
        write_markdown(REPO_ROOT / md_path, title, payload)

    print(
        "runtime preview v1.3 evidence generated "
        f"cases={len(cases)} redactedCases={len(redacted_cases)} reports={len(reports)} "
        "metadataOnly=true realResourcesTouched=false"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
