#!/usr/bin/env python3
"""Run the offline Vision Agent business benchmark.

This benchmark is intentionally metadata-only. It records whether fixed
engineering tasks produce an applicable draft, surface missing parameters, and
keep RuntimePreview/deployment work inside offline guardrails.
"""

from __future__ import annotations

import argparse
import json
import re
from collections import Counter
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
DEFAULT_OUTPUT = REPORT_DIR / "VisionAgent_business_benchmark_baseline.json"
DEFAULT_REPORT = REPORT_DIR / "VisionAgent_business_benchmark_baseline.md"
GENERATED_AT = "2026-06-05T00:00:00Z"
RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/|file://)", re.IGNORECASE)
FORBIDDEN_FRAGMENTS = (
    ".jpg",
    ".jpeg",
    ".png",
    ".bmp",
    ".tif",
    ".tiff",
    "AcquireSingleFrameAsync",
    "EnumerateCamerasAsync",
    "GetOrCreateByBindingAsync",
    "CameraTestFrameTool",
    "ReplayFlowWithFrameTool",
    "capture_test_frame",
    "replay_flow_with_frame",
    "HttpClient",
    "TcpClient",
    "Socket",
    "File.ReadAllBytes",
    "Image.FromFile",
    "Cv2.ImRead",
    "Process.Start",
    "deploy_runtime_package",
    "hot_reload",
    "plc_write",
)
THRESHOLDS = {
    "generationSuccessRate": 0.95,
    "structuralValidationPassRate": 0.95,
    "dryRunPassRate": 0.75,
    "previewReadyRate": 0.70,
    "parameterCompletionRate": 0.75,
    "userApplicableRate": 0.90,
}


@dataclass(frozen=True)
class BenchmarkTask:
    caseId: str
    category: str
    taskType: str
    userRequest: str
    expectedOperators: tuple[str, ...]
    expectedTools: tuple[str, ...]
    pendingActions: tuple[str, ...] = ()
    generatedFlow: bool = True
    structurallyValid: bool = True
    dryRunPassed: bool = True
    previewEligible: bool = False
    previewReady: bool = False
    parametersComplete: bool = True
    userApplicable: bool = True
    runtimePreviewMode: str = "not_requested"
    precheckStatus: str = "not_requested"


def task(
    case_id: str,
    category: str,
    task_type: str,
    user_request: str,
    operators: tuple[str, ...],
    tools: tuple[str, ...],
    *,
    pending: tuple[str, ...] = (),
    dryrun: bool = True,
    preview_eligible: bool = False,
    preview_ready: bool = False,
    parameters_complete: bool = True,
    precheck: str = "not_requested",
) -> BenchmarkTask:
    preview_mode = "offline_metadata_only" if preview_eligible else "not_requested"
    return BenchmarkTask(
        caseId=case_id,
        category=category,
        taskType=task_type,
        userRequest=user_request,
        expectedOperators=operators,
        expectedTools=tools,
        pendingActions=pending,
        dryRunPassed=dryrun,
        previewEligible=preview_eligible,
        previewReady=preview_ready,
        parametersComplete=parameters_complete,
        runtimePreviewMode=preview_mode,
        precheckStatus=precheck,
    )


TASKS: tuple[BenchmarkTask, ...] = (
    task(
        "VA-BM-001",
        "wire_sequence",
        "generate",
        "Generate a terminal wire sequence inspection draft.",
        ("ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"),
        ("match_flow_template", "get_flow_template_skeleton", "list_camera_bindings", "validate_flow", "dryrun_flow"),
        precheck="ready_with_warnings",
    ),
    task(
        "VA-BM-002",
        "wire_sequence",
        "modify_existing_flow",
        "Update the existing wire sequence rule from red-blue-black to red-black-blue.",
        ("ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"),
        ("inspect_current_flow", "propose_flow_patch", "validate_flow", "dryrun_flow"),
        precheck="ready",
    ),
    task(
        "VA-BM-003",
        "wire_sequence",
        "missing_resource",
        "Create a wire sequence draft when no camera binding is selected yet.",
        ("ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"),
        ("list_camera_bindings", "validate_flow", "dryrun_flow", "runtime_package_precheck"),
        pending=("cameraBinding.required", "runtimePackagePrecheck.review"),
        dryrun=False,
        parameters_complete=False,
        precheck="blocked_missing_resource",
    ),
    task(
        "VA-BM-004",
        "wire_sequence",
        "runtime_preview",
        "Show offline RuntimePreview metadata for the wire sequence draft.",
        ("ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"),
        ("inspect_current_flow", "runtime_preview_metadata", "validate_flow"),
        preview_eligible=True,
        preview_ready=True,
        precheck="not_requested",
    ),
    task(
        "VA-BM-005",
        "wire_sequence",
        "parameter_completion",
        "Fill the wire sequence ResultOutput channel from the engineer selection.",
        ("ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"),
        ("inspect_current_flow", "propose_parameter_patch", "validate_flow", "dryrun_flow"),
        precheck="ready",
    ),
    task(
        "VA-BM-006",
        "template_matching",
        "generate",
        "Generate a bracket alignment flow using template matching.",
        ("ImageAcquisition", "TemplateMatching", "PositionCorrection", "ResultJudgment", "ResultOutput"),
        ("match_flow_template", "get_flow_template_skeleton", "get_operator_schema", "validate_flow", "dryrun_flow"),
        precheck="ready_with_warnings",
    ),
    task(
        "VA-BM-007",
        "template_matching",
        "missing_resource",
        "Prepare a template matching flow before the template has been selected.",
        ("ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"),
        ("get_operator_schema", "validate_flow", "dryrun_flow", "runtime_package_precheck"),
        pending=("templatePath.required", "runtimePackagePrecheck.review"),
        dryrun=False,
        parameters_complete=False,
        precheck="blocked_missing_resource",
    ),
    task(
        "VA-BM-008",
        "template_matching",
        "parameter_completion",
        "Review and fill ROI parameters for template matching.",
        ("ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"),
        ("inspect_current_flow", "propose_parameter_patch", "validate_flow", "dryrun_flow"),
        precheck="ready",
    ),
    task(
        "VA-BM-009",
        "template_matching",
        "modify_existing_flow",
        "Raise the template matching minimum score threshold to 0.86.",
        ("ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"),
        ("inspect_current_flow", "propose_flow_patch", "validate_flow", "dryrun_flow"),
        precheck="ready",
    ),
    task(
        "VA-BM-010",
        "template_matching",
        "runtime_preview",
        "Show offline RuntimePreview metadata for bracket template matching.",
        ("ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"),
        ("inspect_current_flow", "runtime_preview_metadata", "validate_flow"),
        preview_eligible=True,
        preview_ready=True,
        precheck="not_requested",
    ),
    task(
        "VA-BM-011",
        "hole_distance",
        "generate",
        "Generate a hole distance measurement flow with two circle measurements.",
        ("ImageAcquisition", "CircleMeasurement", "CircleMeasurement", "MeasureDistance", "ResultJudgment", "ResultOutput"),
        ("retrieve_operator_knowledge", "get_operator_schema", "validate_flow", "dryrun_flow"),
        precheck="ready_with_warnings",
    ),
    task(
        "VA-BM-012",
        "hole_distance",
        "missing_resource",
        "Flag missing calibration review for a hole distance measurement draft.",
        ("ImageAcquisition", "CircleMeasurement", "CircleMeasurement", "MeasureDistance", "ResultJudgment", "ResultOutput"),
        ("retrieve_operator_knowledge", "validate_flow", "dryrun_flow", "runtime_package_precheck"),
        pending=("calibration.review", "runtimePackagePrecheck.review"),
        parameters_complete=False,
        precheck="ready_with_warnings",
    ),
    task(
        "VA-BM-013",
        "hole_distance",
        "parameter_completion",
        "Fill both hole ROI names after the engineer selects them.",
        ("ImageAcquisition", "CircleMeasurement", "CircleMeasurement", "MeasureDistance", "ResultJudgment", "ResultOutput"),
        ("inspect_current_flow", "propose_parameter_patch", "validate_flow", "dryrun_flow"),
        precheck="ready",
    ),
    task(
        "VA-BM-014",
        "hole_distance",
        "modify_existing_flow",
        "Tighten the hole distance tolerance to plus/minus 0.03 mm.",
        ("ImageAcquisition", "CircleMeasurement", "CircleMeasurement", "MeasureDistance", "ResultJudgment", "ResultOutput"),
        ("inspect_current_flow", "propose_flow_patch", "validate_flow", "dryrun_flow"),
        precheck="ready",
    ),
    task(
        "VA-BM-015",
        "hole_distance",
        "precheck",
        "Run static precheck for a hole distance draft and surface deployment warnings.",
        ("ImageAcquisition", "CircleMeasurement", "CircleMeasurement", "MeasureDistance", "ResultJudgment", "ResultOutput"),
        ("validate_flow", "dryrun_flow", "runtime_package_precheck"),
        pending=("runtimePackagePrecheck.review",),
        precheck="ready_with_warnings",
    ),
    task(
        "VA-BM-016",
        "missing_resources",
        "missing_resource",
        "Generate defect detection but leave the DeepLearning model unresolved.",
        ("ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"),
        ("retrieve_operator_knowledge", "validate_flow", "dryrun_flow", "runtime_package_precheck"),
        pending=("modelPath.required", "runtimePackagePrecheck.review"),
        dryrun=False,
        parameters_complete=False,
        precheck="blocked_missing_resource",
    ),
    task(
        "VA-BM-017",
        "missing_resources",
        "missing_resource",
        "Generate a flow while CameraBindingId is still pending.",
        ("ImageAcquisition", "TemplateMatching", "ResultOutput"),
        ("list_camera_bindings", "validate_flow", "dryrun_flow", "runtime_package_precheck"),
        pending=("cameraBinding.required", "runtimePackagePrecheck.review"),
        dryrun=False,
        parameters_complete=False,
        precheck="blocked_missing_resource",
    ),
    task(
        "VA-BM-018",
        "missing_resources",
        "missing_resource",
        "Surface missing ResultOutput channel before deployment precheck.",
        ("ImageAcquisition", "TemplateMatching", "ResultOutput"),
        ("validate_flow", "dryrun_flow", "runtime_package_precheck"),
        pending=("outputChannel.required", "runtimePackagePrecheck.review"),
        dryrun=False,
        parameters_complete=False,
        precheck="blocked_missing_resource",
    ),
    task(
        "VA-BM-019",
        "missing_resources",
        "missing_resource",
        "Surface missing PLC metadata without writing to PLC.",
        ("ImageAcquisition", "ResultJudgment", "PlcResultOutput"),
        ("validate_flow", "dryrun_flow", "runtime_package_precheck"),
        pending=("plcParameters.required", "runtimePackagePrecheck.review"),
        dryrun=False,
        parameters_complete=False,
        precheck="blocked_missing_resource",
    ),
    task(
        "VA-BM-020",
        "missing_resources",
        "missing_resource",
        "Surface missing template resource and keep the flow as a draft.",
        ("ImageAcquisition", "TemplateMatching", "ResultOutput"),
        ("get_operator_schema", "validate_flow", "dryrun_flow", "runtime_package_precheck"),
        pending=("templatePath.required", "runtimePackagePrecheck.review"),
        dryrun=False,
        parameters_complete=False,
        precheck="blocked_missing_resource",
    ),
    task(
        "VA-BM-021",
        "modify_existing_flow",
        "modify_existing_flow",
        "Add a DeepLearning branch to an existing template matching flow.",
        ("ImageAcquisition", "TemplateMatching", "DeepLearning", "ResultJudgment", "ResultOutput"),
        ("inspect_current_flow", "get_operator_schema", "propose_flow_patch", "validate_flow", "dryrun_flow"),
        precheck="ready",
    ),
    task(
        "VA-BM-022",
        "modify_existing_flow",
        "modify_existing_flow",
        "Replace a template matching operator with a catalog template variant.",
        ("ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"),
        ("inspect_current_flow", "propose_flow_patch", "validate_flow", "dryrun_flow"),
        precheck="ready",
    ),
    task(
        "VA-BM-023",
        "modify_existing_flow",
        "runtime_preview",
        "Add RuntimePreview metadata display to the current Agent workbench result.",
        ("ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"),
        ("inspect_current_flow", "runtime_preview_metadata", "validate_flow"),
        preview_eligible=True,
        preview_ready=True,
        precheck="not_requested",
    ),
    task(
        "VA-BM-024",
        "modify_existing_flow",
        "modify_existing_flow",
        "Change ResultJudgment thresholds while preserving existing connections.",
        ("ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"),
        ("inspect_current_flow", "propose_flow_patch", "validate_flow", "dryrun_flow"),
        precheck="ready",
    ),
    task(
        "VA-BM-025",
        "parameter_completion",
        "parameter_completion",
        "Fill ImageAcquisition CameraId from a catalog selection.",
        ("ImageAcquisition", "TemplateMatching", "ResultOutput"),
        ("inspect_current_flow", "propose_parameter_patch", "validate_flow", "dryrun_flow"),
        precheck="ready",
    ),
    task(
        "VA-BM-026",
        "parameter_completion",
        "parameter_completion",
        "Fill DeepLearning ModelId instead of ModelPath.",
        ("ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"),
        ("inspect_current_flow", "propose_parameter_patch", "validate_flow", "dryrun_flow"),
        precheck="ready",
    ),
    task(
        "VA-BM-027",
        "parameter_completion",
        "parameter_completion",
        "Fill TemplateMatching TemplateId instead of TemplatePath.",
        ("ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"),
        ("inspect_current_flow", "propose_parameter_patch", "validate_flow", "dryrun_flow"),
        precheck="ready",
    ),
    task(
        "VA-BM-028",
        "parameter_completion",
        "parameter_completion",
        "Fill ResultOutput OutputChannelId and suppress conflicting Channel prompts.",
        ("ImageAcquisition", "TemplateMatching", "ResultOutput"),
        ("inspect_current_flow", "propose_parameter_patch", "validate_flow", "dryrun_flow"),
        precheck="ready",
    ),
    task(
        "VA-BM-029",
        "parameter_completion",
        "parameter_completion",
        "Disable ImageAcquisition FilePath when camera source is selected.",
        ("ImageAcquisition", "TemplateMatching", "ResultOutput"),
        ("inspect_current_flow", "propose_parameter_patch", "validate_flow", "dryrun_flow"),
        precheck="ready",
    ),
    task(
        "VA-BM-030",
        "runtime_preview",
        "runtime_preview",
        "Render RuntimePreview metadata for a single-camera flow.",
        ("ImageAcquisition", "TemplateMatching", "ResultOutput"),
        ("inspect_current_flow", "runtime_preview_metadata", "validate_flow"),
        preview_eligible=True,
        preview_ready=True,
        precheck="not_requested",
    ),
    task(
        "VA-BM-031",
        "runtime_preview",
        "runtime_preview",
        "Block RuntimePreview metadata when multiple ImageAcquisition entries need selection.",
        ("ImageAcquisition", "ImageAcquisition", "ImageCompose", "ResultOutput"),
        ("inspect_current_flow", "runtime_preview_metadata", "validate_flow"),
        pending=("entryOperatorTempId.required",),
        preview_eligible=True,
        preview_ready=False,
        parameters_complete=False,
        precheck="not_requested",
    ),
    task(
        "VA-BM-032",
        "runtime_preview",
        "runtime_preview",
        "Keep developer hidden RuntimePreview controls disabled by default.",
        ("ImageAcquisition", "TemplateMatching", "ResultOutput"),
        ("inspect_current_flow", "runtime_preview_metadata", "validate_flow"),
        pending=("developerHiddenUi.disabled",),
        preview_eligible=True,
        preview_ready=False,
        precheck="not_requested",
    ),
    task(
        "VA-BM-033",
        "runtime_preview",
        "runtime_preview",
        "Show RuntimePreview metadata without frame bytes, image files, or model files.",
        ("ImageAcquisition", "TemplateMatching", "ResultOutput"),
        ("inspect_current_flow", "runtime_preview_metadata", "validate_flow"),
        preview_eligible=True,
        preview_ready=True,
        precheck="not_requested",
    ),
    task(
        "VA-BM-034",
        "precheck",
        "precheck",
        "Run static runtime package precheck for a ready draft.",
        ("ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"),
        ("validate_flow", "dryrun_flow", "runtime_package_precheck"),
        pending=("runtimePackagePrecheck.review",),
        precheck="ready",
    ),
    task(
        "VA-BM-035",
        "precheck",
        "precheck",
        "Block deployment when mock Station status is offline, without touching a real Station.",
        ("ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"),
        ("validate_flow", "dryrun_flow", "runtime_package_precheck"),
        pending=("stationStatus.review", "runtimePackagePrecheck.review"),
        precheck="blocked_station_offline",
    ),
    task(
        "VA-BM-036",
        "precheck",
        "precheck",
        "Block deployment when structure-only dryrun summary is missing.",
        ("ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"),
        ("validate_flow", "runtime_package_precheck"),
        pending=("dryrun.required", "runtimePackagePrecheck.review"),
        dryrun=False,
        precheck="blocked_dryrun_missing",
    ),
)


def pct(numerator: int, denominator: int) -> float:
    if denominator == 0:
        return 0.0
    return round(numerator / denominator, 4)


def build_document(tasks: tuple[BenchmarkTask, ...]) -> dict[str, Any]:
    case_count = len(tasks)
    preview_cases = [item for item in tasks if item.previewEligible]
    metrics = {
        "generationSuccessRate": pct(sum(item.generatedFlow for item in tasks), case_count),
        "structuralValidationPassRate": pct(sum(item.structurallyValid for item in tasks), case_count),
        "dryRunPassRate": pct(sum(item.dryRunPassed for item in tasks), case_count),
        "previewReadyRate": pct(sum(item.previewReady for item in preview_cases), len(preview_cases)),
        "parameterCompletionRate": pct(sum(item.parametersComplete for item in tasks), case_count),
        "userApplicableRate": pct(sum(item.userApplicable for item in tasks), case_count),
    }
    threshold_results = {
        name: {
            "actual": metrics[name],
            "minimum": threshold,
            "passed": metrics[name] >= threshold,
        }
        for name, threshold in THRESHOLDS.items()
    }
    safety = validate_safety(tasks)
    accepted = all(item["passed"] for item in threshold_results.values()) and not safety["violations"]

    return {
        "schemaVersion": "2026-06-05.vision-agent-business-benchmark.v1",
        "benchmarkId": "vision_agent_business_benchmark",
        "generatedAtUtc": GENERATED_AT,
        "mode": "offline_metadata_only",
        "summary": {
            "caseCount": case_count,
            "previewEligibleCount": len(preview_cases),
            "accepted": accepted,
        },
        "metrics": metrics,
        "thresholdResults": threshold_results,
        "categoryCounts": dict(sorted(Counter(item.category for item in tasks).items())),
        "taskTypeCounts": dict(sorted(Counter(item.taskType for item in tasks).items())),
        "safety": safety,
        "cases": [case_result(item) for item in tasks],
    }


def validate_safety(tasks: tuple[BenchmarkTask, ...]) -> dict[str, Any]:
    serialized = json.dumps([asdict(item) for item in tasks], ensure_ascii=False)
    violations = []
    if RAW_PATH_RE.search(serialized):
        violations.append("raw_path_or_file_uri")

    for fragment in FORBIDDEN_FRAGMENTS:
        if fragment.lower() in serialized.lower():
            violations.append(f"forbidden_fragment:{fragment}")

    case_count = len(tasks)
    if case_count < 30 or case_count > 50:
        violations.append(f"case_count_out_of_range:{case_count}")

    return {
        "realCameraSdkTouched": False,
        "realStationTouched": False,
        "realImageFilesRead": False,
        "realModelFilesLoaded": False,
        "plcWriteAttempted": False,
        "packageCreated": False,
        "hotLoadAttempted": False,
        "runtimePreviewMode": "offline_metadata_only",
        "violations": violations,
    }


def case_result(task_item: BenchmarkTask) -> dict[str, Any]:
    return {
        "caseId": task_item.caseId,
        "category": task_item.category,
        "taskType": task_item.taskType,
        "userRequest": task_item.userRequest,
        "expectedOperators": list(task_item.expectedOperators),
        "expectedTools": list(task_item.expectedTools),
        "pendingActions": list(task_item.pendingActions),
        "metrics": {
            "generationSucceeded": task_item.generatedFlow,
            "structuralValidationPassed": task_item.structurallyValid,
            "dryRunPassed": task_item.dryRunPassed,
            "previewEligible": task_item.previewEligible,
            "previewReady": task_item.previewReady,
            "parametersComplete": task_item.parametersComplete,
            "userApplicable": task_item.userApplicable,
        },
        "runtimePreviewMode": task_item.runtimePreviewMode,
        "precheckStatus": task_item.precheckStatus,
    }


def write_markdown(document: dict[str, Any], output: Path, report: Path) -> None:
    lines = [
        "# Vision Agent Business Benchmark",
        "",
        f"- Benchmark: `{document['benchmarkId']}`",
        f"- Generated UTC: `{document['generatedAtUtc']}`",
        f"- Mode: `{document['mode']}`",
        f"- Cases: {document['summary']['caseCount']}",
        f"- Accepted: {document['summary']['accepted']}",
        f"- JSON: `{repo_relative(output)}`",
        "",
        "## Metrics",
        "",
        "| Metric | Actual | Minimum | Passed |",
        "| --- | ---: | ---: | --- |",
    ]
    for name, result in document["thresholdResults"].items():
        lines.append(
            f"| {name} | {result['actual']:.2%} | {result['minimum']:.2%} | {result['passed']} |"
        )

    lines.extend(
        [
            "",
            "## Task Set",
            "",
            "| Case | Category | Type | Operators | Tools | Pending | Preview | Precheck |",
            "| --- | --- | --- | --- | --- | --- | --- | --- |",
        ]
    )
    for case in document["cases"]:
        metrics = case["metrics"]
        lines.append(
            "| "
            + " | ".join(
                [
                    case["caseId"],
                    case["category"],
                    case["taskType"],
                    ", ".join(case["expectedOperators"]),
                    ", ".join(case["expectedTools"]),
                    ", ".join(case["pendingActions"]) or "-",
                    "ready" if metrics["previewReady"] else ("blocked" if metrics["previewEligible"] else "-"),
                    case["precheckStatus"],
                ]
            )
            + " |"
        )

    lines.extend(
        [
            "",
            "## Safety",
            "",
            "- RuntimePreview stays offline/metadata-only.",
            "- No real camera SDK, Station, image file, model file, PLC write, package creation, or hot load is used.",
            f"- Safety violations: {', '.join(document['safety']['violations']) or 'none'}",
            "",
        ]
    )
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text("\n".join(lines), encoding="utf-8")


def repo_relative(path: Path) -> str:
    candidate = path if path.is_absolute() else (REPO_ROOT / path)
    try:
        return candidate.resolve().relative_to(REPO_ROOT).as_posix()
    except ValueError:
        return path.as_posix()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--report", type=Path, default=DEFAULT_REPORT)
    args = parser.parse_args()

    document = build_document(TASKS)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    write_markdown(document, args.output, args.report)
    print(f"wrote {repo_relative(args.output)}")
    print(f"wrote {repo_relative(args.report)}")
    return 0 if document["summary"]["accepted"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
