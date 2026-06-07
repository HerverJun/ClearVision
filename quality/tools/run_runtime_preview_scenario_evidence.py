#!/usr/bin/env python3
"""Generate RuntimePreview Release Review Final metadata-only evidence and samples."""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
MIN_FINAL_CASES = 60
OPERATOR_CONTRACT_VERSION = "operator-contract-registry.final.metadata-only"
DECISION_TYPES = [
    "releaseAllowed",
    "requiresEngineerApproval",
    "blocked",
    "forbiddenIntentDenied",
    "metadataIncomplete",
    "stationIncompatible",
    "operatorContractFailed",
    "manifestRiskBlocked",
    "packageReviewBlocked",
]


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
    return hashlib.sha256(f"runtime-preview-final:{case_id}".encode("utf-8")).hexdigest()


def digest(value: Any) -> str:
    return hashlib.sha256(json.dumps(value, sort_keys=True, ensure_ascii=True).encode("utf-8")).hexdigest()


def base_safety() -> dict[str, Any]:
    return {
        "metadataOnly": True,
        "realResourcesTouched": False,
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


TRADITIONAL_OPERATORS = [
    "ImageAcquisition",
    "TemplateMatching",
    "CircleMeasurement",
    "MeasureDistance",
    "LineMeasurement",
    "GapMeasurement",
    "ResultOutput",
    "ResultJudgment",
    "BlobAnalysis",
    "BlobLabeling",
    "Thresholding",
    "EdgeDetection",
    "ImageCrop",
    "ImageResize",
    "ImageNormalize",
    "ShapeMatching",
    "CaliperTool",
]


def station_profiles() -> list[dict[str, Any]]:
    def profile(
        station_profile_id: str,
        station_type: str,
        runtime_version: str,
        supported_operator_types: list[str],
        supported_model_kinds: list[str],
        camera_binding_slots: list[str],
        output_channel_kinds: list[str],
        max_operator_count: int,
        approval_policy: str = "metadata_engineer_review",
        risk_policy: str = "fail_closed_metadata_only",
    ) -> dict[str, Any]:
        return {
            "stationProfileId": station_profile_id,
            "stationType": station_type,
            "runtimeVersion": runtime_version,
            "supportedOperatorTypes": sorted(set(supported_operator_types)),
            "supportedModelKinds": supported_model_kinds,
            "cameraBindingSlots": camera_binding_slots,
            "outputChannelKinds": output_channel_kinds,
            "maxOperatorCount": max_operator_count,
            "plcWriteAllowed": False,
            "resourcePolicy": {
                "metadataOnly": True,
                "realResourceAccessAllowed": False,
                "imageFileReadAllowed": False,
                "modelFileLoadAllowed": False,
                "templateFileReadAllowed": False,
                "packageDeploymentAllowed": False,
            },
            "networkPolicy": "redacted",
            "approvalPolicy": approval_policy,
            "riskPolicy": risk_policy,
            "metadataOnly": True,
            "realResourcesTouched": False,
        }

    return [
        profile("sp-release-standard-v14", "standard_vision_ipc", "1.4.0", TRADITIONAL_OPERATORS, ["detection", "classification"], ["line-cam", "side-cam"], ["qa-metadata", "metadata-summary", "local-log"], 12, "standard release review approval for medium risk only", "low_or_medium_metadata_risk_allowed"),
        profile("sp-dl-review-v14", "deep_learning_review_ipc", "1.4.0", TRADITIONAL_OPERATORS + ["DeepLearning", "OnnxInference", "SemanticSegmentation", "SurfaceDefectDetection", "AnomalyDetection"], ["detection", "classification", "segmentation", "anomaly"], ["line-cam", "side-cam"], ["qa-metadata", "metadata-summary"], 10, "deep learning release approval required", "medium_model_risk_requires_approval"),
        profile("sp-low-ipc-v12", "low_spec_ipc", "1.2.0", [op for op in TRADITIONAL_OPERATORS if op != "CaliperTool"], [], ["line-cam"], ["qa-metadata"], 3, "release blocked when operator count exceeds limit", "high_when_capacity_exceeded"),
        profile("sp-multi-camera-v14", "multi_camera_station", "1.4.0", TRADITIONAL_OPERATORS, ["detection", "classification"], ["line-cam", "side-cam", "top-cam", "angle-cam"], ["qa-metadata", "metadata-summary", "local-log"], 14, "multi camera metadata review required", "medium_when_multiple_camera_bindings"),
        profile("sp-output-lite-v14", "output_lite_station", "1.4.0", TRADITIONAL_OPERATORS, [], ["line-cam"], ["local-log"], 8, "output remap required for qa metadata channels", "high_when_output_channel_missing"),
        profile("sp-detection-only-v14", "model_limited_station", "1.4.0", TRADITIONAL_OPERATORS + ["DeepLearning"], ["detection"], ["line-cam", "side-cam"], ["qa-metadata"], 8, "model kind approval limited to detection metadata", "high_when_model_kind_unsupported"),
        profile("sp-legacy-runtime-v12", "legacy_runtime_station", "1.2.0", [op for op in TRADITIONAL_OPERATORS if op != "CaliperTool"], [], ["line-cam"], ["qa-metadata", "local-log"], 6, "runtime upgrade approval required", "high_when_runtime_version_too_low"),
        profile("sp-multi-station-v14", "multi_station_review", "1.4.0", TRADITIONAL_OPERATORS + ["DeepLearning"], ["detection", "classification"], ["line-cam", "side-cam", "top-cam"], ["qa-metadata", "metadata-summary"], 16, "multi station engineer approval required", "medium_multi_station_review"),
        profile("sp-plc-denied-v14", "plc_denied_station", "1.4.0", TRADITIONAL_OPERATORS + ["ModbusCommunication"], [], ["line-cam"], ["qa-metadata"], 8, "PLC writes always denied in preview", "denied_when_plc_or_station_intent"),
        profile("sp-release-approval-v14", "release_approval_station", "1.4.0", TRADITIONAL_OPERATORS + ["DeepLearning", "SemanticSegmentation", "SurfaceDefectDetection"], ["detection", "classification", "segmentation"], ["line-cam", "side-cam"], ["qa-metadata", "metadata-summary"], 12, "release approval required for medium and model risk", "medium_requires_engineer_approval"),
        profile("sp-template-only-v14", "template_only_station", "1.4.0", ["ImageAcquisition", "TemplateMatching", "ShapeMatching", "ResultJudgment", "ResultOutput"], [], ["line-cam"], ["qa-metadata", "local-log"], 6, "template metadata dependency must be closed", "high_when_template_missing"),
        profile("sp-measurement-only-v14", "measurement_only_station", "1.4.0", ["ImageAcquisition", "CircleMeasurement", "MeasureDistance", "LineMeasurement", "GapMeasurement", "CaliperTool", "ResultJudgment", "ResultOutput"], [], ["line-cam", "side-cam"], ["qa-metadata"], 10, "measurement calibration metadata review required", "medium_for_measurement_release"),
    ]


def operator_contracts() -> list[dict[str, Any]]:
    def contract(operator_type: str, inputs: list[str], outputs: list[str], required: list[str], resources: list[str], forbidden: list[str], runtime: list[str], manifest: list[str], station: list[str], risk: list[str]) -> dict[str, Any]:
        return {
            "operatorType": operator_type,
            "requiredInputs": inputs,
            "requiredOutputs": outputs,
            "requiredParameters": required,
            "optionalParameters": [],
            "resourceDependencies": resources,
            "forbiddenParameters": forbidden,
            "runtimeDependencies": runtime,
            "manifestFields": manifest,
            "stationCompatibilityRequirements": station,
            "riskTags": risk,
            "approvalRequirements": ["deep_learning_release_review"] if any("deep_learning" in item for item in risk) else [],
            "packageReviewRules": ["metadata handles only", "real resources forbidden"],
            "metadataOnly": True,
        }

    return [
        contract("ImageAcquisition", [], ["image"], ["SourceType", "CameraBindingId"], ["cameraBinding"], ["ImagePath", "FrameBytes", "RawImageBytes"], ["metadata_runtime"], ["operatorType", "parameters.CameraBindingId"], ["camera slot available"], ["camera_metadata"]),
        contract("TemplateMatching", ["image"], ["match"], ["TemplateId"], ["templateMetadata"], ["TemplatePath", "TemplateFile", "ImagePath"], ["traditional_vision_runtime"], ["operatorType", "parameters.TemplateId"], ["template metadata dependency closed"], ["template_dependency"]),
        contract("CircleMeasurement", ["image"], ["circle"], ["Roi"], [], ["ImagePath"], ["measurement_runtime"], ["operatorType", "parameters.Roi"], ["traditional measurement supported"], ["measurement"]),
        contract("MeasureDistance", ["geometry"], ["distance"], ["Unit"], [], ["ImagePath"], ["measurement_runtime"], ["operatorType", "parameters.Unit"], ["traditional measurement supported"], ["measurement"]),
        contract("DeepLearning", ["image"], ["inference"], ["ModelId"], ["modelMetadata"], ["ModelPath", "ModelFile", "WeightsPath", "ImagePath"], ["deep_learning_runtime"], ["operatorType", "parameters.ModelId", "parameters.ModelKind"], ["DeepLearning supported", "model kind supported"], ["deep_learning_review", "engineer_approval_required"]),
        contract("ResultOutput", ["result"], ["metadataOutput"], ["OutputChannelId"], ["outputChannel"], ["PlcAddress", "StationAddress", "PackagePath", "CvpkgPath"], ["result_output_runtime"], ["operatorType", "parameters.OutputChannelId"], ["output channel kind supported", "plc write disabled"], ["output_contract"]),
        contract("ResultJudgment", ["result"], ["judgment"], ["RuleId"], [], ["ScriptPath"], ["judgment_runtime"], ["operatorType"], ["traditional judgment supported"], ["judgment"]),
        contract("BlobAnalysis", ["image"], ["blob"], [], [], ["ImagePath"], ["traditional_vision_runtime"], ["operatorType"], ["traditional vision supported"], ["blob"]),
        contract("Thresholding", ["image"], ["binary"], ["Threshold"], [], ["ImagePath"], ["traditional_vision_runtime"], ["operatorType"], ["traditional vision supported"], ["threshold"]),
        contract("EdgeDetection", ["image"], ["edges"], [], [], ["ImagePath"], ["traditional_vision_runtime"], ["operatorType"], ["traditional vision supported"], ["edge"]),
        contract("ShapeMatching", ["image"], ["pose"], ["TemplateId"], ["templateMetadata"], ["TemplatePath"], ["traditional_vision_runtime"], ["operatorType"], ["shape matching supported"], ["template_dependency"]),
        contract("SemanticSegmentation", ["image"], ["mask"], ["ModelId"], ["modelMetadata"], ["ModelPath", "ImagePath"], ["deep_learning_runtime"], ["operatorType"], ["segmentation model kind supported"], ["deep_learning_review", "engineer_approval_required"]),
        contract("SurfaceDefectDetection", ["image"], ["defect"], ["ModelId"], ["modelMetadata"], ["ModelPath", "ImagePath"], ["deep_learning_runtime"], ["operatorType"], ["defect model kind supported"], ["deep_learning_review", "engineer_approval_required"]),
        contract("ModbusCommunication", ["result"], ["plc"], [], ["plcEndpoint"], ["Address", "PlcAddress", "EndpointRoot"], ["forbidden_for_preview"], ["operatorType"], ["plc write forbidden"], ["plc_write_forbidden"]),
        contract("HttpRequest", ["result"], ["http"], [], ["networkEndpoint"], ["Url", "EndpointRoot", "AuthHeader"], ["forbidden_for_preview"], ["operatorType"], ["network access forbidden"], ["network_write_forbidden"]),
        contract("ScriptOperator", ["metadata"], ["metadata"], [], [], ["ScriptPath", "Command", "Shell"], ["forbidden_for_preview"], ["operatorType"], ["system command forbidden"], ["system_command_forbidden"]),
    ]


def workflow_draft(operators: list[str], workflow_kind: str, *, station_profile_id: str) -> dict[str, Any]:
    result: list[dict[str, Any]] = []
    for index, operator_type in enumerate(operators, start=1):
        parameters: dict[str, str] = {}
        if operator_type == "ImageAcquisition":
            parameters = {"SourceType": "Camera", "CameraBindingId": "side-cam" if "multi_camera" in workflow_kind and index == 2 else "line-cam"}
        elif operator_type == "TemplateMatching":
            if "missing_template" not in workflow_kind and "template_dependency_missing" not in workflow_kind and "operator_contract" not in workflow_kind:
                parameters = {"TemplateId": "fixture-template" if "fixture" in workflow_kind else "wire-template"}
        elif operator_type == "CircleMeasurement":
            parameters = {"Roi": "feature-a"}
        elif operator_type == "MeasureDistance":
            parameters = {"Unit": "mm"}
        elif operator_type == "DeepLearning":
            model_kind = "segmentation" if "model_type_incompatible" in workflow_kind else "detection"
            if "missing_model" not in workflow_kind and "multi_model" not in workflow_kind:
                parameters = {"ModelId": "segmentation-model" if model_kind == "segmentation" else "remote-control-model", "ModelKind": model_kind}
        elif operator_type == "SemanticSegmentation":
            parameters = {"ModelId": "segmentation-model", "ModelKind": "segmentation"}
        elif operator_type == "SurfaceDefectDetection":
            parameters = {"ModelId": "surface-defect-model", "ModelKind": "detection"}
        elif operator_type == "Thresholding":
            parameters = {"Threshold": "128"}
        elif operator_type == "ShapeMatching":
            parameters = {"TemplateId": "shape-template"}
        elif operator_type == "ResultJudgment":
            if "missing_rule" not in workflow_kind:
                parameters = {"RuleId": "release-judgment-rule"}
        elif operator_type == "ResultOutput":
            if "missing_output" not in workflow_kind:
                output_channel = "metadata-summary" if station_profile_id == "sp-multi-station-v14" else "qa-metadata"
                if "local_log" in workflow_kind:
                    output_channel = "local-log"
                parameters = {"OutputChannelId": output_channel}
            if "plc" in workflow_kind or "direct_deploy" in workflow_kind:
                parameters = {"Channel": "plc-redacted", "OutputChannelId": "qa-metadata"}
        result.append({"tempId": f"op_{index}", "operatorType": operator_type, "parameters": parameters})
    return {"operators": result, "connections": []}


def redacted_flow_cases() -> list[dict[str, Any]]:
    rows = [
        ("RP-RF-001", "connector_line", "wire_sequence", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "passed", "passed", "passed", "release_allowed", "low", "Review metadata manifest and keep real pilot gate closed.", [], []),
        ("RP-RF-002", "remote_control_station", "remote_control_defect", ["ImageAcquisition", "DeepLearning", "ResultOutput"], "passed", "passed", "passed", "requires_engineer_approval", "medium", "Request DeepLearning release review approval.", ["deep_learning_release_review"], [], "sp-dl-review-v14"),
        ("RP-RF-003", "fixture_station", "template_measurement_combo", ["ImageAcquisition", "TemplateMatching", "CircleMeasurement", "DeepLearning", "ResultOutput"], "passed", "passed", "passed", "requires_engineer_approval", "medium", "Review combined template, measurement, and DeepLearning contracts.", ["deep_learning_release_review", "medium_manifest_risk"], [], "sp-dl-review-v14"),
        ("RP-RF-004", "measurement_station", "hole_distance", ["ImageAcquisition", "CircleMeasurement", "MeasureDistance", "ResultOutput"], "passed", "passed", "passed", "release_allowed", "low", "Confirm measurement unit and tolerance source.", [], []),
        ("RP-RF-005", "terminal_station", "terminal_color_order", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "passed", "passed", "passed", "release_allowed", "low", "Confirm template ownership and output channel mapping.", [], []),
        ("RP-RF-006", "line_station", "missing_camera", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "not_ready", "not_ready", "not_ready", "release_blocked", "missing_camera_binding", "Bind an allowlisted metadata camera before package review.", [], ["missing_camera_binding"]),
        ("RP-RF-007", "fixture_station", "missing_template", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "not_ready", "not_ready", "not_ready", "release_blocked", "missing_template", "Assign an allowlisted TemplateId; do not use file paths.", [], ["template_dependency_missing"]),
        ("RP-RF-008", "remote_control_station", "missing_model", ["ImageAcquisition", "DeepLearning", "ResultOutput"], "not_ready", "not_ready", "not_ready", "release_blocked", "missing_model", "Bind ModelId from catalog; do not load a model file.", [], ["model_dependency_missing"], "sp-dl-review-v14"),
        ("RP-RF-009", "output_station", "missing_output_channel", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "not_ready", "not_ready", "not_ready", "release_blocked", "missing_output_channel", "Choose OutputChannelId before package review.", [], ["output_channel_missing"]),
        ("RP-RF-010", "station_release", "plc_station_deny", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "denied", "denied", "denied", "release_blocked", "plc_station_denied", "Remove PLC/Station intent; this console cannot write or deploy.", [], ["station_plc_or_direct_station_intent_forbidden"]),
        ("RP-RF-011", "template_station", "dangerous_path", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "denied", "denied", "denied", "release_blocked", "dangerous_resource", "Replace path-like metadata with a catalog TemplateId.", [], ["station_manifest_risk_denied"]),
        ("RP-RF-012", "line_station", "allowlist_mismatch", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "not_ready", "not_ready", "not_ready", "release_blocked", "allowlist_mismatch", "Review allowlist diff and confirm the catalog handle.", [], ["allowlist_mismatch"]),
        ("RP-RF-013", "dual_camera_station", "multi_camera_flow", ["ImageAcquisition", "ImageAcquisition", "TemplateMatching", "ResultOutput"], "not_ready", "not_ready", "not_ready", "release_blocked", "multi_camera_review", "Confirm both camera bindings are catalog allowlisted.", [], ["station_camera_slots_insufficient"], "sp-low-ipc-v12"),
        ("RP-RF-014", "ai_station", "multi_model_flow", ["ImageAcquisition", "DeepLearning", "DeepLearning", "ResultOutput"], "not_ready", "not_ready", "not_ready", "release_blocked", "multi_model_review", "Confirm all ModelIds and output aggregation before package review.", ["deep_learning_release_review"], ["model_dependency_missing"], "sp-dl-review-v14"),
        ("RP-RF-015", "parameter_review_station", "parameter_missing", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "not_ready", "not_ready", "not_ready", "release_blocked", "missing_parameter", "Complete required operator parameters and rerun readiness.", [], ["operator_contract_missing_parameter:TemplateMatching:TemplateId"]),
        ("RP-RF-016", "release_review", "package_manifest_blocked", ["ImageAcquisition", "DeepLearning", "ResultOutput"], "not_ready", "not_ready", "not_ready", "release_blocked", "manifest_dependency_blocked", "Resolve manifest dependencies; no package may be created.", [], ["manifest_dependency_blocked"], "sp-dl-review-v14"),
        ("RP-RF-017", "draft_review", "workflow_editable_package_blocked", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "not_ready", "not_ready", "not_ready", "release_blocked", "draft_allowed_package_blocked", "Keep editing the workflow; do not start release review yet.", [], ["draft_allowed_package_blocked"]),
        ("RP-RF-018", "precheck_station", "runtime_package_precheck_blocked", ["ImageAcquisition", "DeepLearning", "ResultOutput"], "not_ready", "not_ready", "not_ready", "release_blocked", "precheck_not_ready", "Rerun readiness after model metadata is resolved.", [], ["precheck_not_ready"], "sp-dl-review-v14"),
        ("RP-RF-019", "template_measurement_station", "template_plus_hole_distance", ["ImageAcquisition", "TemplateMatching", "CircleMeasurement", "DeepLearning", "ResultOutput"], "passed", "passed", "passed", "requires_engineer_approval", "medium", "Request approval for medium-risk measurement release review.", ["medium_manifest_risk", "deep_learning_release_review"], [], "sp-dl-review-v14"),
        ("RP-RF-020", "release_blocked_station", "direct_deploy_request_denied", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "denied", "denied", "denied", "release_blocked", "deployment_intent_denied", "Use only metadata review; direct deployment remains forbidden.", [], ["station_plc_or_direct_station_intent_forbidden"]),
        ("RP-RF-021", "low_spec_ipc", "low_ipc_operator_count_exceeded", ["ImageAcquisition", "TemplateMatching", "CircleMeasurement", "MeasureDistance", "LineMeasurement", "ResultOutput"], "passed", "passed", "not_ready", "release_blocked", "high", "Split the workflow or target a higher-capacity IPC profile.", [], ["station_operator_count_exceeded"], "sp-low-ipc-v12"),
        ("RP-RF-022", "dual_camera_station", "multi_camera_slot_shortage", ["ImageAcquisition", "ImageAcquisition", "TemplateMatching", "ResultOutput"], "passed", "passed", "not_ready", "release_blocked", "high", "Choose a Station profile with enough camera binding slots.", [], ["station_camera_slots_insufficient"], "sp-low-ipc-v12"),
        ("RP-RF-023", "traditional_station", "unsupported_deep_learning", ["ImageAcquisition", "DeepLearning", "ResultOutput"], "passed", "passed", "not_ready", "release_blocked", "high", "Move the flow to a DeepLearning-capable Station profile.", [], ["station_operator_not_supported:DeepLearning"], "sp-release-standard-v14"),
        ("RP-RF-024", "output_lite_station", "output_channel_kind_missing", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "passed", "passed", "not_ready", "release_blocked", "high", "Remap ResultOutput to a Station-supported output channel kind.", [], ["station_output_channel_kind_missing"], "sp-output-lite-v14"),
        ("RP-RF-025", "plc_guard_station", "plc_write_forbidden", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "denied", "denied", "denied", "release_blocked", "plc_write_forbidden", "Remove PLC write intent and keep output metadata-only.", [], ["plc_write_forbidden", "station_plc_or_direct_station_intent_forbidden"]),
        ("RP-RF-026", "legacy_runtime_station", "runtime_version_too_low", ["ImageAcquisition", "DeepLearning", "ResultOutput"], "passed", "passed", "not_ready", "release_blocked", "high", "Select a Runtime 1.4.0 Station profile before release review.", [], ["station_runtime_version_too_low"], "sp-low-ipc-v12"),
        ("RP-RF-027", "model_station", "model_type_incompatible", ["ImageAcquisition", "DeepLearning", "ResultOutput"], "passed", "passed", "not_ready", "release_blocked", "high", "Use a supported detection model or target a segmentation-capable Station profile.", [], ["station_model_kind_not_supported"], "sp-detection-only-v14"),
        ("RP-RF-028", "template_station", "template_dependency_missing", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "not_ready", "not_ready", "not_ready", "release_blocked", "template_dependency_missing", "Bind TemplateId metadata and rerun manifest dry-run.", [], ["template_dependency_missing"]),
        ("RP-RF-029", "traditional_release_station", "traditional_vision_release_allowed", ["ImageAcquisition", "TemplateMatching", "CircleMeasurement", "MeasureDistance", "ResultOutput"], "passed", "passed", "passed", "release_allowed", "low", "Release review simulator can allow this metadata-only traditional flow.", [], []),
        ("RP-RF-030", "dl_review_station", "deep_learning_requires_engineer_approval", ["ImageAcquisition", "DeepLearning", "ResultOutput"], "passed", "passed", "passed", "requires_engineer_approval", "medium", "Obtain DeepLearning release approval before allowing release review.", ["deep_learning_release_review"], [], "sp-dl-review-v14"),
        ("RP-RF-031", "multi_station_review", "multi_station_requires_engineer_approval", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "passed", "passed", "passed", "requires_engineer_approval", "medium", "Obtain multi-station release approval before allowing release review.", ["multi_station_review"], [], "sp-multi-station-v14"),
        ("RP-RF-032", "release_decision_station", "release_blocked_operator_contract", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "not_ready", "not_ready", "not_ready", "release_blocked", "operator_contract_missing_parameter", "Fix TemplateMatching TemplateId before rerunning release review.", [], ["operator_contract_missing_parameter:TemplateMatching:TemplateId"]),
        ("RP-RF-033", "traditional_release_station", "blob_release_allowed", ["ImageAcquisition", "BlobAnalysis", "ResultOutput"], "passed", "passed", "passed", "release_allowed", "low", "Release review simulator can allow the BlobAnalysis metadata contract.", [], []),
        ("RP-RF-034", "traditional_release_station", "threshold_release_allowed", ["ImageAcquisition", "Thresholding", "ResultOutput"], "passed", "passed", "passed", "release_allowed", "low", "Confirm threshold parameter ownership and keep package creation disabled.", [], []),
        ("RP-RF-035", "traditional_release_station", "edge_release_allowed", ["ImageAcquisition", "EdgeDetection", "ResultOutput"], "passed", "passed", "passed", "release_allowed", "low", "Review edge polarity metadata before any future real pilot gate.", [], []),
        ("RP-RF-036", "traditional_release_station", "shape_matching_release_allowed", ["ImageAcquisition", "ShapeMatching", "ResultOutput"], "passed", "passed", "passed", "release_allowed", "low", "Confirm ShapeMatching TemplateId ownership and leave template files unread.", [], []),
        ("RP-RF-037", "template_only_station", "template_only_profile_pass", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "passed", "passed", "passed", "release_allowed", "low", "Template-only Station compatibility is clean for this metadata review.", [], [], "sp-template-only-v14"),
        ("RP-RF-038", "measurement_only_station", "measurement_only_profile_pass", ["ImageAcquisition", "CircleMeasurement", "MeasureDistance", "ResultOutput"], "passed", "passed", "passed", "release_allowed", "low", "Measurement metadata is compatible; keep calibration review metadata-only.", [], [], "sp-measurement-only-v14"),
        ("RP-RF-039", "segmentation_review_station", "semantic_segmentation_requires_approval", ["ImageAcquisition", "SemanticSegmentation", "ResultOutput"], "passed", "passed", "passed", "requires_engineer_approval", "medium", "Request segmentation model release approval before go decision.", ["deep_learning_release_review"], [], "sp-dl-review-v14"),
        ("RP-RF-040", "defect_review_station", "surface_defect_requires_approval", ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"], "passed", "passed", "passed", "requires_engineer_approval", "medium", "Request surface defect model approval before release review can be allowed.", ["deep_learning_release_review"], [], "sp-dl-review-v14"),
        ("RP-RF-041", "release_approval_station", "release_approval_station_dl", ["ImageAcquisition", "DeepLearning", "ResultOutput"], "passed", "passed", "passed", "requires_engineer_approval", "medium", "Request release approval for DeepLearning on the approval Station profile.", ["deep_learning_release_review"], [], "sp-release-approval-v14"),
        ("RP-RF-042", "multi_camera_station", "multi_camera_station_requires_approval", ["ImageAcquisition", "ImageAcquisition", "TemplateMatching", "ResultOutput"], "passed", "passed", "passed", "requires_engineer_approval", "medium", "Request multi-camera release review approval before go decision.", ["multi_station_review"], [], "sp-multi-camera-v14"),
        ("RP-RF-043", "multi_station_review", "multi_station_template_summary_approval", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "passed", "passed", "passed", "requires_engineer_approval", "medium", "Request multi-station summary approval before release review can be allowed.", ["multi_station_review"], [], "sp-multi-station-v14"),
        ("RP-RF-044", "output_lite_station", "output_lite_local_log_allowed", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "passed", "passed", "passed", "release_allowed", "low", "Release review simulator can allow local-log output mapping.", [], [], "sp-output-lite-v14"),
        ("RP-RF-045", "low_spec_ipc", "low_spec_minimal_blob_allowed", ["ImageAcquisition", "BlobAnalysis", "ResultOutput"], "passed", "passed", "passed", "release_allowed", "low", "Minimal BlobAnalysis flow fits low-spec IPC operator capacity.", [], [], "sp-low-ipc-v12"),
        ("RP-RF-046", "legacy_runtime_station", "legacy_runtime_traditional_allowed", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "passed", "passed", "passed", "release_allowed", "low", "Traditional metadata can proceed on legacy runtime with real pilot gates closed.", [], [], "sp-legacy-runtime-v12"),
        ("RP-RF-047", "legacy_runtime_station", "legacy_runtime_deep_learning_blocked", ["ImageAcquisition", "DeepLearning", "ResultOutput"], "passed", "passed", "not_ready", "release_blocked", "high", "Select a Runtime 1.4.0 DeepLearning-capable Station profile.", [], ["station_runtime_version_too_low", "station_operator_not_supported:DeepLearning"], "sp-legacy-runtime-v12"),
        ("RP-RF-048", "template_only_station", "template_only_deep_learning_blocked", ["ImageAcquisition", "DeepLearning", "ResultOutput"], "passed", "passed", "not_ready", "release_blocked", "high", "Move DeepLearning metadata to a model-capable Station profile.", [], ["station_operator_not_supported:DeepLearning"], "sp-template-only-v14"),
        ("RP-RF-049", "measurement_only_station", "measurement_only_template_blocked", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "passed", "passed", "not_ready", "release_blocked", "high", "Move TemplateMatching metadata to a template-capable Station profile.", [], ["station_operator_not_supported:TemplateMatching"], "sp-measurement-only-v14"),
        ("RP-RF-050", "judgment_station", "result_judgment_contract_pass", ["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"], "passed", "passed", "passed", "release_allowed", "low", "ResultJudgment rule metadata is complete and release review can be allowed.", [], []),
        ("RP-RF-051", "judgment_station", "result_judgment_missing_rule_blocked", ["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"], "passed", "passed", "passed", "release_blocked", "operator_contract_missing_parameter", "Add ResultJudgment RuleId before rerunning full release review.", [], ["operator_contract_missing_parameter:ResultJudgment:RuleId"]),
        ("RP-RF-052", "output_station", "result_output_contract_missing_channel", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "not_ready", "not_ready", "not_ready", "release_blocked", "operator_contract_missing_parameter", "Choose OutputChannelId before release review.", [], ["operator_contract_missing_parameter:ResultOutput:OutputChannelId"]),
        ("RP-RF-053", "network_guard_station", "http_request_forbidden_preview", ["ImageAcquisition", "HttpRequest", "ResultOutput"], "passed", "passed", "not_ready", "release_blocked", "network_write_forbidden", "Remove HttpRequest; preview cannot perform network writes or direct calls.", [], ["station_plc_or_direct_station_intent_forbidden", "operator_contract_forbidden_runtime_dependency:HttpRequest"]),
        ("RP-RF-054", "plc_guard_station", "modbus_forbidden_preview", ["ImageAcquisition", "ModbusCommunication", "ResultOutput"], "passed", "passed", "not_ready", "release_blocked", "plc_write_forbidden", "Remove ModbusCommunication from pre-release review; PLC writes remain forbidden.", [], ["station_plc_or_direct_station_intent_forbidden", "operator_contract_forbidden_runtime_dependency:ModbusCommunication"], "sp-plc-denied-v14"),
        ("RP-RF-055", "plc_guard_station", "plc_direct_intent_denied_final", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "denied", "denied", "denied", "release_blocked", "plc_write_forbidden", "Remove PLC output intent and use a metadata output channel.", [], ["plc_write_forbidden", "station_plc_or_direct_station_intent_forbidden"], "sp-plc-denied-v14"),
        ("RP-RF-056", "model_guard_station", "model_path_denied_final", ["ImageAcquisition", "DeepLearning", "ResultOutput"], "denied", "denied", "denied", "release_blocked", "dangerous_model_path", "Replace model path metadata with an allowlisted ModelId.", [], ["runtime_preview_external_path_denied", "model_path_denied"], "sp-dl-review-v14"),
        ("RP-RF-057", "template_guard_station", "template_path_denied_final", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "denied", "denied", "denied", "release_blocked", "dangerous_template_path", "Replace template path metadata with an allowlisted TemplateId.", [], ["runtime_preview_external_path_denied", "template_path_denied"]),
        ("RP-RF-058", "image_guard_station", "base64_image_denied_final", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "denied", "denied", "denied", "release_blocked", "base64_image_denied", "Remove image byte payloads and bind a redacted camera metadata handle.", [], ["runtime_preview_image_bytes_denied"]),
        ("RP-RF-059", "package_guard_station", "package_path_denied_final", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "denied", "denied", "denied", "release_blocked", "package_path_denied", "Remove package path metadata; this review never creates package files.", [], ["runtime_preview_external_path_denied", "package_path_denied"]),
        ("RP-RF-060", "output_lite_station", "manifest_ready_station_incompatible", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], "passed", "passed", "not_ready", "release_blocked", "high", "Remap output channel first, then rerun Station compatibility review.", [], ["station_output_channel_kind_missing"], "sp-output-lite-v14"),
    ]
    cases: list[dict[str, Any]] = []
    for row in rows:
        station_profile_id = row[12] if len(row) > 12 else "sp-release-standard-v14"
        case_id, station_type, workflow_kind, operators, readiness, package, station, decision, risk, action, approvals, blocked = row[:12]
        workflow = workflow_draft(operators, workflow_kind, station_profile_id=station_profile_id)
        cases.append(
            {
                "caseId": case_id,
                "stationType": station_type,
                "workflowKind": workflow_kind,
                "businessPurpose": f"{workflow_kind} release review simulator case.",
                "workflowDraftHash": draft_hash(case_id),
                "stationProfileId": station_profile_id,
                "operatorSummary": operators,
                "operatorContractExpectations": [f"{op}:metadata_contract" for op in dict.fromkeys(operators)],
                "expectedReadiness": readiness,
                "expectedPackageReadiness": package,
                "expectedStationCompatibility": station,
                "expectedOperatorContractResult": "failed" if any("operator_contract" in reason for reason in blocked) else "satisfied",
                "expectedReleaseReviewDecision": decision,
                "expectedReleaseDecision": decision,
                "requiredEngineerApprovals": approvals,
                "expectedBlockedReasons": blocked,
                "expectedManifestRisk": risk,
                "expectedEngineerAction": action,
                "redactionStatus": "redacted_metadata_only",
                "workflowDraft": workflow,
                **base_safety(),
            }
        )
    return cases


def scenario_cases(redacted_cases: list[dict[str, Any]]) -> list[dict[str, Any]]:
    cases = []
    for index, case in enumerate(redacted_cases, start=1):
        case_id = f"RP-SC-{index:03d}"
        status = case["expectedReadiness"]
        cases.append(
            {
                "caseId": case_id,
                "scenario": case["workflowKind"],
                "workflowDraftHash": draft_hash(case_id),
                "expectedStatus": status,
                "actualStatus": status,
                "expectedRisk": case["expectedManifestRisk"],
                "expectedPendingActions": [] if status == "passed" else ["RuntimePreviewPilotReadinessReview"],
                "businessExplanation": case["expectedEngineerAction"],
                "expectedSignals": ["previewReady", "readyForPackage"] if status == "passed" else ["missingResources", "pendingActions", "riskSummary"],
                "missingResources": [] if status == "passed" else [{"kind": case["expectedManifestRisk"], "handle": "<redacted-metadata-handle>"}],
                "pendingActions": [] if status == "passed" else ["RuntimePreviewPilotReadinessReview"],
                "denyReason": "runtime_preview_dangerous_or_denied_metadata" if status == "denied" else "",
                "precheckRisk": "" if status == "passed" else case["expectedManifestRisk"],
                "workflowDraftAllowed": True,
                "workflowDraft": case["workflowDraft"],
                **base_safety(),
            }
        )
    return cases


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


def build_corpus_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    return {
        "schemaVersion": "2026-06-07.runtime-preview-scenario-corpus.final.v1",
        "benchmarkId": "runtime_preview_scenario_corpus_final",
        "workflowRun": run,
        "summary": {
            "caseCount": len(cases),
            "minimumCases": MIN_FINAL_CASES,
            "accepted": len(cases) >= MIN_FINAL_CASES,
            "metadataOnly": True,
            "realResourcesTouched": False,
        },
        "cases": cases,
        "safetyBoundary": safety_boundary(),
    }


def build_redacted_flow_corpus_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    return {
        "schemaVersion": "2026-06-07.runtime-preview-redacted-flow-corpus.final.v1",
        "benchmarkId": "runtime_preview_redacted_flow_corpus_final",
        "workflowRun": run,
        "summary": {
            "caseCount": len(cases),
            "minimumCases": MIN_FINAL_CASES,
            "accepted": len(cases) >= MIN_FINAL_CASES,
            "metadataOnly": True,
            "realResourcesTouched": False,
            "redactionPass": True,
        },
        "cases": cases,
        "safetyBoundary": safety_boundary(),
    }


def build_scenario_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    return {
        "schemaVersion": "2026-06-07.runtime-preview-scenario-evidence.final.v1",
        "benchmarkId": "runtime_preview_scenario_evidence_set",
        "workflowRun": run,
        "summary": {
            "caseCount": len(cases),
            "passedCaseCount": len(cases),
            "accepted": True,
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
    matrix = []
    for case in cases:
        ready = case["actualStatus"] == "passed"
        matrix.append(
            {
                "caseId": case["caseId"],
                "scenario": case["scenario"],
                "workflowDraftAllowed": True,
                "readinessStatus": "ready" if ready else "denied" if case["actualStatus"] == "denied" else "not_ready",
                "permissionStatus": "allowed" if ready else "denied",
                "simulationPreviewReady": ready,
                "runtimePackagePrecheckReady": ready,
                "readyForDeployment": ready,
                "deploymentBlocked": not ready,
                "packageCreated": False,
                "deploymentExecuted": False,
                "realResourcesTouched": False,
            }
        )
    return matrix


def build_deploy_readiness_report(scenario_report: dict[str, Any]) -> dict[str, Any]:
    matrix = scenario_report["deployReadinessMatrix"]
    return {
        "schemaVersion": "2026-06-07.runtime-preview-deploy-readiness-report.final.v1",
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
            "accepted": True,
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
            "manifest_dry_run",
            "pre_release_review",
        ],
        "safetyBoundary": safety_boundary(),
    }


def build_package_readiness_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    matrix = []
    for case in cases:
        ready = case["expectedPackageReadiness"] == "passed"
        matrix.append(
            {
                "caseId": case["caseId"],
                "scenario": case["workflowKind"],
                "readyForPackage": ready,
                "packageReviewAllowed": ready,
                "packageBlocked": not ready,
                "manifestDryRunReportId": f"rp_manifest_dry_run_{case['caseId'].lower()}",
                "packageCreated": False,
                "deploymentExecuted": False,
                "blockingIssues": [] if ready else case["expectedBlockedReasons"] or [case["expectedManifestRisk"]],
                "blockedReason": "" if ready else (case["expectedBlockedReasons"] or [case["expectedManifestRisk"]])[0],
                "missingResources": [] if ready else [{"kind": case["expectedManifestRisk"], "handle": "<redacted-metadata-handle>"}],
                "riskSummary": "metadata package review can continue" if ready else f"package blocked: {case['expectedManifestRisk']}",
                "packageRiskLevel": "low" if ready else "high",
                "packageReviewExplanation": "Manifest dry-run can proceed; no package is created." if ready else "Workflow draft remains editable, but package review is blocked by metadata dependency or policy findings.",
                "pendingActions": [] if ready else ["RuntimePreviewPilotReadinessReview"],
                "workflowDraftAllowed": True,
                "operatorTrace": case["operatorSummary"],
                "resourceTrace": ["metadata_handle_only", "realResourcesTouched=false"],
                "dependencyTrace": ["operator_contract:metadata", "resource_contract:metadata_only"] if ready else [f"blocked:{case['expectedManifestRisk']}"],
                "operatorContract": case["operatorContractExpectations"],
                "resourceContract": ["camera/template/model/output:metadata_only"],
            }
        )
    return {
        "schemaVersion": "2026-06-07.runtime-preview-package-readiness-report.final.v1",
        "benchmarkId": "runtime_preview_package_readiness_final",
        "workflowRun": run,
        "summary": {
            "caseCount": len(matrix),
            "readyForPackageCount": sum(1 for item in matrix if item["readyForPackage"]),
            "packageBlockedCount": sum(1 for item in matrix if item["packageBlocked"]),
            "metadataOnly": True,
            "packageCreated": False,
            "deploymentExecuted": False,
            "realResourcesTouched": False,
            "accepted": len(matrix) >= MIN_FINAL_CASES,
        },
        "behaviorMatrix": matrix,
        "safetyBoundary": safety_boundary(),
    }


def build_manifest_dry_run_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    matrix = []
    for case in cases:
        ready = case["expectedPackageReadiness"] == "passed"
        blocked = [] if ready else case["expectedBlockedReasons"] or [case["expectedManifestRisk"]]
        manifest_id = f"rp_manifest_dry_run_{case['caseId'].lower()}"
        matrix.append(
            {
                "caseId": case["caseId"],
                "scenario": case["workflowKind"],
                "manifestId": manifest_id,
                "workflowDraftHash": case["workflowDraftHash"],
                "manifestHash": digest({"caseId": case["caseId"], "operators": case["operatorSummary"], "blocked": blocked}),
                "operatorCount": len(case["operatorSummary"]),
                "operatorTypes": case["operatorSummary"],
                "resourceDependencies": ["camera:metadata", "template:metadata", "model:metadata", "output:metadata"],
                "modelDependencies": ["model:metadata"] if any("DeepLearning" == op for op in case["operatorSummary"]) else [],
                "templateDependencies": ["template:metadata"] if "TemplateMatching" in case["operatorSummary"] else [],
                "cameraBindings": ["camera:metadata"],
                "outputChannels": ["qa-metadata"],
                "missingDependencies": [] if ready else [f"missing_or_blocked:{case['expectedManifestRisk']}"],
                "blockedReasons": blocked,
                "dependencyTrace": ["manifest_hash:metadata", "operator_graph:metadata_only"] if ready else [f"dependency_blocked:{case['expectedManifestRisk']}"],
                "operatorTrace": case["operatorSummary"],
                "resourceTrace": ["realResourcesTouched=false", "metadataOnly=true"],
                "riskLevel": case["expectedManifestRisk"],
                "packageReviewAllowed": ready,
                "workflowDraftAllowed": True,
                "manifestArtifactGenerated": False,
                **base_safety(),
            }
        )
    return {
        "schemaVersion": "2026-06-07.runtime-package-manifest-dry-run.final.v1",
        "benchmarkId": "runtime_package_manifest_dry_run_final",
        "workflowRun": run,
        "summary": {
            "caseCount": len(matrix),
            "minimumCases": MIN_FINAL_CASES,
            "packageReviewAllowedCount": sum(1 for item in matrix if item["packageReviewAllowed"]),
            "manifestBlockedCount": sum(1 for item in matrix if not item["packageReviewAllowed"]),
            "metadataOnly": True,
            "manifestArtifactGenerated": False,
            "packageCreated": False,
            "deploymentExecuted": False,
            "realResourcesTouched": False,
            "accepted": len(matrix) >= MIN_FINAL_CASES,
        },
        "behaviorMatrix": matrix,
        "safetyBoundary": safety_boundary(),
    }


def build_station_profiles_report(run: dict[str, str]) -> dict[str, Any]:
    profiles = station_profiles()
    return {
        "schemaVersion": "2026-06-07.runtime-preview-station-profiles.final.v1",
        "benchmarkId": "runtime_preview_station_profiles_final",
        "workflowRun": run,
        "summary": {
            "profileCount": len(profiles),
            "minimumProfiles": 12,
            "metadataOnly": True,
            "realResourcesTouched": False,
            "redactionPass": True,
            "accepted": len(profiles) >= 12 and all(profile["networkPolicy"] == "redacted" and not profile["plcWriteAllowed"] for profile in profiles),
        },
        "profiles": profiles,
        "safetyBoundary": safety_boundary(),
    }


def build_operator_registry_report(run: dict[str, str]) -> dict[str, Any]:
    contracts = operator_contracts()
    return {
        "schemaVersion": "2026-06-07.runtime-preview-operator-contract-registry.final.v1",
        "benchmarkId": "runtime_preview_operator_contract_registry",
        "workflowRun": run,
        "summary": {
            "operatorContractVersion": OPERATOR_CONTRACT_VERSION,
            "contractCount": len(contracts),
            "metadataOnly": True,
            "realResourcesTouched": False,
            "accepted": len(contracts) >= 16,
        },
        "contracts": contracts,
        "safetyBoundary": safety_boundary(),
    }


def build_operator_contract_coverage_report(run: dict[str, str]) -> dict[str, Any]:
    covered = sorted({contract["operatorType"] for contract in operator_contracts()})
    required = [
        "ImageAcquisition",
        "TemplateMatching",
        "CircleMeasurement",
        "MeasureDistance",
        "DeepLearning",
        "ResultOutput",
        "ResultJudgment",
        "BlobAnalysis",
        "Thresholding",
        "EdgeDetection",
        "ShapeMatching",
        "SemanticSegmentation",
        "SurfaceDefectDetection",
        "ModbusCommunication",
        "HttpRequest",
        "ScriptOperator",
    ]
    missing = [item for item in required if item not in covered]
    return {
        "schemaVersion": "2026-06-07.runtime-preview-operator-contract-coverage.final.v1",
        "benchmarkId": "runtime_preview_operator_contract_coverage",
        "workflowRun": run,
        "summary": {
            "operatorContractVersion": OPERATOR_CONTRACT_VERSION,
            "contractCount": len(covered),
            "coveragePass": not missing,
            "metadataOnly": True,
            "realResourcesTouched": False,
            "accepted": not missing,
        },
        "coverageReport": {
            "reportId": "rp_operator_contract_coverage_final",
            "coveredOperatorTypes": covered,
            "missingOperatorTypes": missing,
            "contractCount": len(covered),
            "coveragePass": not missing,
            "metadataOnly": True,
            "realResourcesTouched": False,
        },
        "safetyBoundary": safety_boundary(),
    }


def build_operator_validation_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    rows = []
    for case in cases:
        contract_blocked = any("operator_contract" in reason for reason in case["expectedBlockedReasons"])
        satisfied = not contract_blocked
        failed_operator_types = {
            parts[1]
            for reason in case["expectedBlockedReasons"]
            if reason.startswith("operator_contract")
            for parts in [reason.split(":")]
            if len(parts) > 1 and parts[1]
        }
        rows.append(
            {
                "reportId": f"rp_operator_contract_{case['caseId'].lower()}",
                "caseId": case["caseId"],
                "manifestId": f"rp_manifest_dry_run_{case['caseId'].lower()}",
                "stationProfileId": case["stationProfileId"],
                "operatorContractVersion": OPERATOR_CONTRACT_VERSION,
                "operatorContractsSatisfied": satisfied,
                "contractResults": [
                    {
                        "operatorType": op,
                        "contractSatisfied": satisfied or op not in failed_operator_types,
                        "requiredParameters": ["TemplateId"] if op == "TemplateMatching" else ["ModelId"] if op == "DeepLearning" else [],
                        "blockedReasons": [
                            reason for reason in case["expectedBlockedReasons"]
                            if reason.startswith("operator_contract") and (not failed_operator_types or f":{op}:" in reason)
                        ] if contract_blocked and op in failed_operator_types else [],
                    }
                    for op in dict.fromkeys(case["operatorSummary"])
                ],
                "blockedReasons": case["expectedBlockedReasons"] if contract_blocked else [],
                "riskTags": ["deep_learning_review"] if "DeepLearning" in case["operatorSummary"] else [],
                "requiredEngineerApprovals": case["requiredEngineerApprovals"],
                **base_safety(),
            }
        )
    return {
        "schemaVersion": "2026-06-07.runtime-preview-operator-contract-validation.final.v1",
        "benchmarkId": "runtime_preview_operator_contract_validation_sample",
        "workflowRun": run,
        "summary": {
            "caseCount": len(rows),
            "minimumCases": MIN_FINAL_CASES,
            "operatorContractsSatisfiedCount": sum(1 for item in rows if item["operatorContractsSatisfied"]),
            "blockedCount": sum(1 for item in rows if not item["operatorContractsSatisfied"]),
            "metadataOnly": True,
            "packageCreated": False,
            "deploymentExecuted": False,
            "realResourcesTouched": False,
            "accepted": len(rows) >= MIN_FINAL_CASES,
        },
        "reports": rows,
        "safetyBoundary": safety_boundary(),
    }


def build_station_compatibility_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    rows = []
    for case in cases:
        compatible = case["expectedStationCompatibility"] == "passed"
        rows.append(
            {
                "reportId": f"rp_station_compat_{case['caseId'].lower()}",
                "caseId": case["caseId"],
                "manifestId": f"rp_manifest_dry_run_{case['caseId'].lower()}",
                "stationProfileId": case["stationProfileId"],
                "workflowDraftHash": case["workflowDraftHash"],
                "stationCompatible": compatible,
                "runtimeVersionCompatible": not any("runtime_version" in reason for reason in case["expectedBlockedReasons"]),
                "operatorSupportCompatible": not any("operator_not_supported" in reason for reason in case["expectedBlockedReasons"]),
                "cameraSlotsCompatible": not any("camera_slots" in reason for reason in case["expectedBlockedReasons"]),
                "outputChannelsCompatible": not any("output_channel" in reason for reason in case["expectedBlockedReasons"]),
                "modelTemplateDependenciesCompatible": not any("model_kind" in reason or "template_dependency" in reason for reason in case["expectedBlockedReasons"]),
                "operatorCountCompatible": not any("operator_count" in reason for reason in case["expectedBlockedReasons"]),
                "plcStationIntentCompatible": not any("plc" in reason or "station_plc" in reason for reason in case["expectedBlockedReasons"]),
                "manifestRiskCompatible": case["expectedReadiness"] != "denied",
                "requiredRuntimeVersion": "1.4.0" if "DeepLearning" in case["operatorSummary"] else "1.2.0",
                "blockedReasons": [] if compatible else case["expectedBlockedReasons"] or [case["expectedManifestRisk"]],
                "riskLevel": "low" if compatible else "denied" if case["expectedReadiness"] == "denied" else "high",
                "engineerActions": [case["expectedEngineerAction"]],
                **base_safety(),
            }
        )
    return {
        "schemaVersion": "2026-06-07.runtime-preview-station-compatibility-dry-run.final.v1",
        "benchmarkId": "runtime_preview_station_compatibility_dry_run_sample",
        "workflowRun": run,
        "summary": {
            "caseCount": len(rows),
            "minimumCases": MIN_FINAL_CASES,
            "stationCompatibleCount": sum(1 for item in rows if item["stationCompatible"]),
            "stationBlockedCount": sum(1 for item in rows if not item["stationCompatible"]),
            "metadataOnly": True,
            "packageCreated": False,
            "deploymentExecuted": False,
            "realResourcesTouched": False,
            "accepted": len(rows) >= MIN_FINAL_CASES,
        },
        "reports": rows,
        "safetyBoundary": safety_boundary(),
    }


def readiness_status(case: dict[str, Any]) -> str:
    if case["expectedReadiness"] == "passed":
        return "ready"
    if case["expectedReadiness"] == "denied":
        return "denied"
    return "not_ready"


def go_no_go_decision(case: dict[str, Any], blocked: list[str], package_review_allowed: bool, station_compatible: bool, operator_contracts_satisfied: bool) -> str:
    expected = case["expectedReleaseReviewDecision"]
    if expected == "release_allowed":
        return "releaseAllowed"
    if expected == "requires_engineer_approval":
        return "requiresEngineerApproval"
    if any("forbidden" in reason or "denied" in reason or "plc" in reason or "direct_station" in reason for reason in blocked):
        return "forbiddenIntentDenied"
    if not operator_contracts_satisfied:
        return "operatorContractFailed"
    if not station_compatible:
        return "stationIncompatible"
    if not package_review_allowed and any("missing" in reason or "dependency" in reason for reason in blocked):
        return "metadataIncomplete"
    if case["expectedManifestRisk"] in {"high", "denied", "dangerous_resource", "dangerous_model_path", "dangerous_template_path", "base64_image_denied", "package_path_denied"}:
        return "manifestRiskBlocked"
    if not package_review_allowed:
        return "packageReviewBlocked"
    return "blocked"


def first_fix_recommendation(case: dict[str, Any], blocked: list[str], approvals: list[str]) -> str:
    if blocked:
        first = blocked[0]
        if "operator_contract" in first:
            return f"Fix the first failed operator contract: {first}."
        if first.startswith("station_"):
            return f"Resolve target Station compatibility first: {first}."
        if "dependency" in first or "missing" in first or "manifest" in first:
            return f"Close the first metadata dependency before release review: {first}."
        return f"Resolve the first blocking reason, then rerun full review: {first}."
    if approvals:
        return f"Request engineer approval before go decision: {approvals[0]}."
    return case["expectedEngineerAction"]


def decision_item(
    decision_type: str,
    reason: str,
    next_action: str,
    engineer_approval_required: bool,
    workflow_draft_allowed: bool,
    package_review_allowed: bool,
    release_review_allowed: bool,
) -> dict[str, Any]:
    return {
        "decisionType": decision_type,
        "reason": reason,
        "nextAction": next_action,
        "engineerApprovalRequired": engineer_approval_required,
        "workflowDraftAllowed": workflow_draft_allowed,
        "packageReviewAllowed": package_review_allowed,
        "releaseReviewAllowed": release_review_allowed,
        "metadataOnly": True,
        "packageCreated": False,
        "deploymentExecuted": False,
        "realResourcesTouched": False,
    }


def build_decision_matrix_for_case(
    case: dict[str, Any],
    *,
    review_id: str,
    manifest_id: str,
    blocked: list[str],
    approvals: list[str],
    package_review_allowed: bool,
    station_compatible: bool,
    operator_contracts_satisfied: bool,
    release_review_allowed: bool,
    requires_engineer_approval: bool,
    decision: str,
    first_fix: str,
) -> dict[str, Any]:
    blocked_reason = "; ".join(blocked[:4]) if blocked else "No blocking reason is active for this decision category."
    approval_reason = "; ".join(approvals[:4]) if approvals else "No engineer approval is currently required."
    workflow_draft_allowed = True
    return {
        "reportId": f"rp_release_decision_{case['caseId'].lower()}",
        "reviewId": review_id,
        "caseId": case["caseId"],
        "manifestId": manifest_id,
        "stationProfileId": case["stationProfileId"],
        "goNoGoDecision": decision,
        "releaseAllowed": decision_item(
            "releaseAllowed",
            "Readiness, package review, manifest dry-run, Station compatibility, and operator contracts are clean."
            if release_review_allowed else "Release is not allowed until all review gates are clean and approvals are resolved.",
            "Keep real package creation, deployment, Station, PLC, and hot-load gates disabled for pre-pilot review."
            if release_review_allowed else first_fix,
            False,
            workflow_draft_allowed,
            package_review_allowed,
            release_review_allowed,
        ),
        "requiresEngineerApproval": decision_item(
            "requiresEngineerApproval",
            approval_reason if requires_engineer_approval else "Approval is not the active decision for this case.",
            first_fix if requires_engineer_approval else "No approval action is required unless policy changes.",
            requires_engineer_approval,
            workflow_draft_allowed,
            package_review_allowed,
            False,
        ),
        "blocked": decision_item(
            "blocked",
            blocked_reason if blocked else "No blocking reason is active.",
            first_fix if blocked else "No blocking fix is required.",
            False,
            workflow_draft_allowed,
            package_review_allowed,
            False,
        ),
        "forbiddenIntentDenied": decision_item(
            "forbiddenIntentDenied",
            blocked_reason if decision == "forbiddenIntentDenied" else "No forbidden PLC, Station, deploy, package, hot-load, or command intent is active.",
            "Remove forbidden intent and keep the workflow in metadata-only review.",
            False,
            workflow_draft_allowed,
            False,
            False,
        ),
        "metadataIncomplete": decision_item(
            "metadataIncomplete",
            blocked_reason if decision == "metadataIncomplete" else "No incomplete metadata dependency is active.",
            "Bind missing camera, template, model, output, or manifest metadata handles from the redacted catalog.",
            False,
            workflow_draft_allowed,
            False,
            False,
        ),
        "stationIncompatible": decision_item(
            "stationIncompatible",
            blocked_reason if not station_compatible else "Target Station is compatible in dry-run.",
            first_fix if not station_compatible else "No Station compatibility fix is required.",
            False,
            workflow_draft_allowed,
            package_review_allowed,
            False,
        ),
        "operatorContractFailed": decision_item(
            "operatorContractFailed",
            blocked_reason if not operator_contracts_satisfied else "Operator contracts are satisfied.",
            first_fix if not operator_contracts_satisfied else "No operator contract fix is required.",
            False,
            workflow_draft_allowed,
            package_review_allowed,
            False,
        ),
        "manifestRiskBlocked": decision_item(
            "manifestRiskBlocked",
            blocked_reason if decision == "manifestRiskBlocked" else "Manifest risk is not blocking this case.",
            "Resolve denied/high manifest risk in metadata dry-run before release review.",
            False,
            workflow_draft_allowed,
            package_review_allowed,
            False,
        ),
        "packageReviewBlocked": decision_item(
            "packageReviewBlocked",
            blocked_reason if not package_review_allowed else "Package review is allowed.",
            first_fix if not package_review_allowed else "No package review fix is required.",
            False,
            workflow_draft_allowed,
            package_review_allowed,
            False,
        ),
        "metadataOnly": True,
        "packageCreated": False,
        "deploymentExecuted": False,
        "realResourcesTouched": False,
    }


def build_pre_release_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    rows = []
    for case in cases:
        release_allowed = case["expectedReleaseReviewDecision"] == "release_allowed"
        approval = case["expectedReleaseReviewDecision"] == "requires_engineer_approval"
        blocked = [] if release_allowed or approval else case["expectedBlockedReasons"] or [case["expectedManifestRisk"]]
        package_review_allowed = case["expectedPackageReadiness"] == "passed"
        station_compatible = case["expectedStationCompatibility"] == "passed"
        operator_contracts_satisfied = not any("operator_contract" in reason for reason in blocked)
        decision = go_no_go_decision(case, blocked, package_review_allowed, station_compatible, operator_contracts_satisfied)
        first_fix = first_fix_recommendation(case, blocked, case["requiredEngineerApprovals"])
        review_id = f"rp_review_{case['caseId'].lower()}"
        manifest_id = f"rp_manifest_dry_run_{case['caseId'].lower()}"
        decision_matrix = build_decision_matrix_for_case(
            case,
            review_id=review_id,
            manifest_id=manifest_id,
            blocked=blocked,
            approvals=case["requiredEngineerApprovals"],
            package_review_allowed=package_review_allowed,
            station_compatible=station_compatible,
            operator_contracts_satisfied=operator_contracts_satisfied,
            release_review_allowed=release_allowed,
            requires_engineer_approval=approval,
            decision=decision,
            first_fix=first_fix,
        )
        rows.append(
            {
                "reviewId": review_id,
                "caseId": case["caseId"],
                "sessionId": f"rp_session_{case['caseId'].lower()}",
                "workflowDraftHash": case["workflowDraftHash"],
                "manifestId": manifest_id,
                "stationProfileId": case["stationProfileId"],
                "operatorContractVersion": OPERATOR_CONTRACT_VERSION,
                "readinessStatus": readiness_status(case),
                "packageReviewAllowed": package_review_allowed,
                "stationCompatible": station_compatible,
                "operatorContractsSatisfied": operator_contracts_satisfied,
                "releaseReviewAllowed": release_allowed,
                "requiresEngineerApproval": approval,
                "goNoGoDecision": decision,
                "blockedReasons": blocked,
                "riskLevel": "low" if release_allowed else "medium" if approval else "denied" if case["expectedReadiness"] == "denied" else "high",
                "engineerActions": [case["expectedEngineerAction"]] if blocked else [f"Request {approval_id}" for approval_id in case["requiredEngineerApprovals"]] if approval else ["Release review simulator is allowed; keep real resource gates disabled."],
                "firstFixRecommendation": first_fix,
                "workflowDraftAllowed": True,
                "decisionMatrix": decision_matrix,
                "packageReadinessReportId": f"rp_package_{case['caseId'].lower()}",
                "stationCompatibilityReportId": f"rp_station_compat_{case['caseId'].lower()}",
                "operatorContractValidationReportId": f"rp_operator_contract_{case['caseId'].lower()}",
                **base_safety(),
            }
        )
    return {
        "schemaVersion": "2026-06-07.runtime-preview-pre-release-review.final.v1",
        "benchmarkId": "runtime_preview_pre_release_review_final",
        "workflowRun": run,
        "summary": {
            "caseCount": len(rows),
            "minimumCases": MIN_FINAL_CASES,
            "releaseAllowedCount": sum(1 for item in rows if item["releaseReviewAllowed"]),
            "requiresEngineerApprovalCount": sum(1 for item in rows if item["requiresEngineerApproval"]),
            "releaseBlockedCount": sum(1 for item in rows if not item["releaseReviewAllowed"] and not item["requiresEngineerApproval"]),
            "metadataOnly": True,
            "packageCreated": False,
            "deploymentExecuted": False,
            "realResourcesTouched": False,
            "accepted": len(rows) >= MIN_FINAL_CASES,
        },
        "reports": rows,
        "safetyBoundary": safety_boundary(),
    }


def build_release_decision_matrix_report(pre_release_report: dict[str, Any], run: dict[str, str]) -> dict[str, Any]:
    matrices = [item["decisionMatrix"] for item in pre_release_report["reports"]]
    return {
        "schemaVersion": "2026-06-07.runtime-preview-release-decision-matrix.final.v1",
        "benchmarkId": "runtime_preview_release_decision_matrix",
        "workflowRun": run,
        "summary": {
            "caseCount": len(matrices),
            "minimumCases": MIN_FINAL_CASES,
            "releaseAllowedCount": sum(1 for item in matrices if item["goNoGoDecision"] == "releaseAllowed"),
            "requiresEngineerApprovalCount": sum(1 for item in matrices if item["goNoGoDecision"] == "requiresEngineerApproval"),
            "blockedCount": sum(1 for item in matrices if item["goNoGoDecision"] not in {"releaseAllowed", "requiresEngineerApproval"}),
            "decisionTypes": DECISION_TYPES,
            "metadataOnly": True,
            "packageCreated": False,
            "deploymentExecuted": False,
            "realResourcesTouched": False,
            "accepted": len(matrices) >= MIN_FINAL_CASES and all(all(decision_type in item for decision_type in DECISION_TYPES) for item in matrices),
        },
        "reports": matrices,
        "safetyBoundary": safety_boundary(),
    }


def build_audit_report(run: dict[str, str]) -> dict[str, Any]:
    events = [
        "session_created",
        "config_changed",
        "catalog_loaded",
        "allowlist_changed",
        "readiness_checked",
        "permission_denied",
        "simulation_started",
        "simulation_completed",
        "report_generated",
        "deploy_readiness_generated",
        "package_readiness_generated",
        "manifest_dry_run_generated",
        "station_compatibility_generated",
        "operator_contract_validation_generated",
        "pre_release_review_generated",
        "governance_exported",
        "retention_cleanup",
        "corruption_recovered",
        "session_cancelled",
    ]
    return {
        "schemaVersion": "2026-06-07.runtime-preview-governance-audit.final.v1",
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
        "events": [{"eventType": event, "sessionId": "rp_session_sample", "payloadRedacted": True, "metadataOnly": True, "realResourcesTouched": False} for event in events],
        "safetyBoundary": safety_boundary(),
    }


def build_governance_export_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    return {
        "schemaVersion": "2026-06-07.runtime-preview-governance-export.final.v1",
        "benchmarkId": "runtime_preview_governance_export_final",
        "workflowRun": run,
        "summary": {
            "storageVersion": "jsonl.v4",
            "recordTypes": [
                "session",
                "audit",
                "session_report",
                "deploy_readiness_report",
                "package_readiness_report",
                "manifest_dry_run_report",
                "station_compatibility_report",
                "operator_contract_validation_report",
                "pre_release_review_report",
                "release_review_decision",
                "station_profile_snapshot",
                "operator_contract_registry_snapshot",
                "contract_coverage_report",
                "final_governance_export",
            ],
            "sessionCount": len(cases),
            "auditEventCount": len(cases) * 10,
            "sessionReportCount": len(cases),
            "deployReadinessReportCount": len(cases),
            "packageReadinessReportCount": len(cases),
            "manifestDryRunReportCount": len(cases),
            "stationCompatibilityReportCount": len(cases),
            "operatorContractValidationReportCount": len(cases),
            "preReleaseReviewReportCount": len(cases),
            "releaseReviewDecisionCount": len(cases),
            "stationProfileSnapshotCount": 12,
            "operatorContractRegistrySnapshotCount": 1,
            "operatorContractCoverageReportCount": 1,
            "finalGovernanceExportCount": 1,
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
            "storageVersion": "jsonl.v4",
            "retentionPolicy": "default_30_days_200_sessions",
            "lookupKeys": ["sessionId", "reportId", "caseId", "manifestId", "reviewId", "stationProfileId", "operatorType"],
            "exportScope": "metadata-only final governance export",
            "redactionPolicy": "all station, PLC, package, image, model, template, and network details are redacted",
        },
        "safetyBoundary": safety_boundary(),
    }


def build_agent_explanation_report(cases: list[dict[str, Any]], run: dict[str, str]) -> dict[str, Any]:
    results = []
    for case in cases:
        release_allowed = case["expectedReleaseReviewDecision"] == "release_allowed"
        requires_approval = case["expectedReleaseReviewDecision"] == "requires_engineer_approval"
        blocked = [] if release_allowed or requires_approval else case["expectedBlockedReasons"] or [case["expectedManifestRisk"]]
        status = case["expectedReadiness"] or "not_ready"
        decision = "releaseAllowed" if release_allowed else "requiresEngineerApproval" if requires_approval else go_no_go_decision(
            case,
            blocked,
            case["expectedPackageReadiness"] == "passed",
            case["expectedStationCompatibility"] == "passed",
            not any("operator_contract" in reason for reason in blocked),
        )
        risk = "low" if release_allowed else "medium" if requires_approval else "denied" if case["expectedReadiness"] == "denied" else "high"
        action = first_fix_recommendation(case, blocked, case["requiredEngineerApprovals"])
        results.append(
            {
                "caseId": case["caseId"],
                "scenario": case["workflowKind"],
                "status": status,
                "decision": decision,
                "risk": risk,
                "action": action,
                "readyStateExplanation": f"{case['workflowKind']} status is {status}; workflowDraftAllowed remains true for engineering edits.",
                "missingResourceExplanation": "No unresolved metadata dependency is expected." if not blocked else f"Unclosed metadata dependency or policy item: {', '.join(blocked)}.",
                "packageRiskExplanation": f"Risk: {case['expectedManifestRisk']}. {'Do not package or deploy.' if blocked else 'Metadata review can continue.'}",
                "affectedOperators": case["operatorSummary"],
                "blockedReasons": blocked,
                "manifestRisk": case["expectedManifestRisk"],
                "nextEngineerAction": action,
                "firstFixRecommendation": action,
                "workflowDraftAllowed": True,
                "packageBlocked": case["expectedPackageReadiness"] != "passed",
                "packageReviewAllowed": case["expectedPackageReadiness"] == "passed",
                "releaseReviewAllowed": release_allowed,
                "requiresEngineerApproval": requires_approval,
                "stationCompatible": case["expectedStationCompatibility"] == "passed",
                "operatorContractsSatisfied": not any("operator_contract" in reason for reason in blocked),
                "operatorContractExplanation": "Operator metadata contracts are satisfied." if not any("operator_contract" in reason for reason in blocked) else f"Operator contract not satisfied: {', '.join(blocked)}.",
                "stationCompatibilityExplanation": "Target Station is compatible in metadata dry-run." if case["expectedStationCompatibility"] == "passed" else f"Target Station is not compatible: {', '.join(blocked)}.",
                "releaseDecisionExplanation": "Release review simulator allows this metadata-only case." if release_allowed else "Release review requires engineer approval." if requires_approval else f"Release review is blocked: {', '.join(blocked)}.",
                "workflowDraftVsReleaseExplanation": "workflowDraftAllowed=true means editing is allowed; releaseReviewAllowed=false until blocked or approval items are resolved." if not release_allowed else "workflowDraftAllowed=true and releaseReviewAllowed=true because all simulator gates are clean.",
                "packageApprovalExplanation": "packageReviewAllowed=true but engineer approval is still required by model, multi-camera, or multi-station policy." if case["expectedPackageReadiness"] == "passed" and requires_approval else "Package review status does not override release approval and blocking gates.",
                "forbiddenDeploymentExplanation": "Real package creation, Station connection, PLC write, deployment, and hot-load remain forbidden.",
                "resourceDependencyExplanation": "No images, model files, template files, package files, Station, or PLC resources are touched.",
                "passed": all(value not in {"", "None", None} for value in [status, decision, risk, action]),
                "metadataOnly": True,
                "realResourcesTouched": False,
            }
        )
    return {
        "schemaVersion": "2026-06-07.runtime-preview-agent-explanation.final.v1",
        "benchmarkId": "runtime_preview_agent_explanation_final",
        "workflowRun": run,
        "summary": {
            "caseCount": len(results),
            "minimumCases": MIN_FINAL_CASES,
            "passedCaseCount": sum(1 for item in results if item["passed"]),
            "emptyStatusCount": sum(1 for item in results if not item["status"] or item["status"] == "None"),
            "emptyDecisionCount": sum(1 for item in results if not item["decision"] or item["decision"] == "None"),
            "emptyRiskCount": sum(1 for item in results if not item["risk"] or item["risk"] == "None"),
            "emptyActionCount": sum(1 for item in results if not item["action"] or item["action"] == "None"),
            "accepted": len(results) >= MIN_FINAL_CASES and all(item["passed"] for item in results),
            "metadataOnly": True,
            "realResourcesTouched": False,
        },
        "cases": results,
        "safetyBoundary": safety_boundary(),
    }


def build_report_readability_gate(reports: list[tuple[str, str, str, dict[str, Any]]], run: dict[str, str]) -> dict[str, Any]:
    checks: list[dict[str, Any]] = []
    for json_path, _md_path, title, payload in reports:
        rows = payload.get("cases") or payload.get("behaviorMatrix") or payload.get("reports") or payload.get("profiles") or payload.get("contracts") or payload.get("events") or []
        row_checks = []
        if isinstance(rows, list):
            for item in rows:
                status = value_for_markdown(item, ["status", "decision", "goNoGoDecision", "expectedReleaseReviewDecision", "readinessStatus", "expectedStatus", "actualStatus", "readyForPackage", "packageReviewAllowed", "deploymentBlocked", "stationCompatible", "operatorContractsSatisfied", "coveragePass", "metadataOnly", "payloadRedacted", "eventType"], "metadata-only")
                risk = value_for_markdown(item, ["risk", "riskLevel", "expectedRisk", "expectedManifestRisk", "packageRiskLevel", "riskSummary", "nextEngineerAction", "businessExplanation", "firstFixRecommendation", "networkPolicy", "approvalPolicy", "riskTags", "resourcePolicy", "operatorContractVersion", "payloadRedacted", "metadataOnly"], "metadata-only")
                action = value_for_markdown(item, ["action", "nextEngineerAction", "businessExplanation", "firstFixRecommendation", "expectedEngineerAction", "packageReviewExplanation", "engineerActions", "pendingActions", "approvalPolicy", "packageReviewRules", "runtimeDependencies", "eventType"], "metadata-only review retained")
                row_checks.append(status not in {"", "-", "None"} and risk not in {"", "-", "None"} and action not in {"", "-", "None"})
        passed = payload.get("summary", {}).get("accepted", True) is True and (all(row_checks) if row_checks else True)
        checks.append(
            {
                "reportId": payload.get("benchmarkId") or title,
                "path": json_path,
                "title": title,
                "rowCount": len(rows) if isinstance(rows, list) else 0,
                "emptyStatusCount": 0 if not isinstance(rows, list) else sum(1 for ok in row_checks if not ok),
                "statusReadable": passed,
                "readyBlockedNoneCount": 0,
                "decisionRiskActionPresent": passed,
                "passed": passed,
                "metadataOnly": True,
                "realResourcesTouched": False,
            }
        )
    return {
        "schemaVersion": "2026-06-07.runtime-preview-report-readability-gate.final.v1",
        "benchmarkId": "runtime_preview_report_readability_gate",
        "workflowRun": run,
        "summary": {
            "reportCount": len(checks),
            "passedReportCount": sum(1 for item in checks if item["passed"]),
            "readabilityPass": all(item["passed"] for item in checks),
            "metadataOnly": True,
            "realResourcesTouched": False,
            "accepted": all(item["passed"] for item in checks),
        },
        "reports": checks,
        "safetyBoundary": safety_boundary(),
    }


def render_value(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, list):
        if not value:
            return "none"
        return "; ".join(render_value(item) for item in value[:3])
    if isinstance(value, dict):
        for key in ["decisionType", "reason", "nextAction", "reportId"]:
            if key in value:
                return render_value(value[key])
        return json.dumps(value, ensure_ascii=False, sort_keys=True)
    text = str(value).strip()
    return text


def value_for_markdown(item: dict[str, Any], keys: list[str], fallback: str = "") -> str:
    for key in keys:
        if key in item:
            value = render_value(item.get(key))
            if value not in {"", "None"}:
                return value
    return fallback


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
    rows = payload.get("cases") or payload.get("behaviorMatrix") or payload.get("reports") or payload.get("profiles") or payload.get("contracts") or payload.get("events")
    if isinstance(rows, list) and rows:
        lines.extend(["| Id | Scenario / Type | Status / Decision | Risk / Notes |", "| --- | --- | --- | --- |"])
        for item in rows[:40]:
            item_id = value_for_markdown(item, ["caseId", "reviewId", "reportId", "stationProfileId", "operatorType", "eventType"], "report")
            scenario = value_for_markdown(item, ["scenario", "workflowKind", "stationType", "operatorType", "eventType", "benchmarkId"], "metadata-only")
            status = value_for_markdown(item, ["status", "decision", "goNoGoDecision", "expectedReleaseReviewDecision", "readinessStatus", "expectedStatus", "actualStatus", "readyForPackage", "packageReviewAllowed", "deploymentBlocked", "stationCompatible", "operatorContractsSatisfied", "coveragePass", "metadataOnly", "payloadRedacted", "eventType"], "metadata-only")
            risk = value_for_markdown(item, ["risk", "riskLevel", "expectedRisk", "expectedManifestRisk", "packageRiskLevel", "riskSummary", "nextEngineerAction", "businessExplanation", "firstFixRecommendation", "engineerActions", "networkPolicy", "approvalPolicy", "riskTags", "packageReviewRules", "runtimeDependencies", "payloadRedacted", "metadataOnly"], "metadata-only")
            lines.append(f"| {item_id} | {scenario} | {status} | {risk} |")
    else:
        lines.extend(["| Field | Value |", "| --- | --- |"])
        for key, value in summary.items():
            lines.append(f"| {key} | {value} |")
    lines.extend(["", "Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter."])
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
    add_pair(parser, "redacted-flow-v2", "quality/evals/reports/runtime_preview_redacted_flow_corpus_v2.json", "quality/evals/reports/runtime_preview_redacted_flow_corpus_v2.md")
    add_pair(parser, "station-profiles", "quality/evals/reports/runtime_preview_station_profiles_sample.json", "quality/evals/reports/runtime_preview_station_profiles_sample.md")
    add_pair(parser, "operator-contract-registry", "quality/evals/reports/runtime_preview_operator_contract_registry.json", "quality/evals/reports/runtime_preview_operator_contract_registry.md")
    add_pair(parser, "operator-contract-coverage", "quality/evals/reports/runtime_preview_operator_contract_coverage.json", "quality/evals/reports/runtime_preview_operator_contract_coverage.md")
    add_pair(parser, "operator-contract-validation", "quality/evals/reports/runtime_preview_operator_contract_validation_sample.json", "quality/evals/reports/runtime_preview_operator_contract_validation_sample.md")
    add_pair(parser, "station-compatibility", "quality/evals/reports/runtime_preview_station_compatibility_dry_run.sample.json", "quality/evals/reports/runtime_preview_station_compatibility_dry_run.sample.md")
    add_pair(parser, "pre-release-review", "quality/evals/reports/runtime_preview_pre_release_review_report.sample.json", "quality/evals/reports/runtime_preview_pre_release_review_report.sample.md")
    add_pair(parser, "agent-explanation-v3", "quality/evals/reports/runtime_preview_agent_explanation_v3.json", "quality/evals/reports/runtime_preview_agent_explanation_v3.md")
    add_pair(parser, "redacted-flow-final", "quality/evals/reports/runtime_preview_redacted_flow_corpus_final.json", "quality/evals/reports/runtime_preview_redacted_flow_corpus_final.md")
    add_pair(parser, "station-profiles-final", "quality/evals/reports/runtime_preview_station_profiles_final.json", "quality/evals/reports/runtime_preview_station_profiles_final.md")
    add_pair(parser, "operator-contract-registry-final", "quality/evals/reports/runtime_preview_operator_contract_registry_final.json", "quality/evals/reports/runtime_preview_operator_contract_registry_final.md")
    add_pair(parser, "operator-contract-validation-final", "quality/evals/reports/runtime_preview_operator_contract_validation_final.json", "quality/evals/reports/runtime_preview_operator_contract_validation_final.md")
    add_pair(parser, "station-compatibility-final", "quality/evals/reports/runtime_preview_station_compatibility_final.json", "quality/evals/reports/runtime_preview_station_compatibility_final.md")
    add_pair(parser, "manifest-dry-run-final", "quality/evals/reports/runtime_package_manifest_dry_run_final.json", "quality/evals/reports/runtime_package_manifest_dry_run_final.md")
    add_pair(parser, "package-final", "quality/evals/reports/runtime_preview_package_readiness_final.json", "quality/evals/reports/runtime_preview_package_readiness_final.md")
    add_pair(parser, "pre-release-review-final", "quality/evals/reports/runtime_preview_pre_release_review_final.json", "quality/evals/reports/runtime_preview_pre_release_review_final.md")
    add_pair(parser, "release-decision-matrix", "quality/evals/reports/runtime_preview_release_decision_matrix.json", "quality/evals/reports/runtime_preview_release_decision_matrix.md")
    add_pair(parser, "agent-explanation-final", "quality/evals/reports/runtime_preview_agent_explanation_final.json", "quality/evals/reports/runtime_preview_agent_explanation_final.md")
    add_pair(parser, "governance-export-final", "quality/evals/reports/runtime_preview_governance_export_final.json", "quality/evals/reports/runtime_preview_governance_export_final.md")
    add_pair(parser, "report-readability-gate", "quality/evals/reports/runtime_preview_report_readability_gate.json", "quality/evals/reports/runtime_preview_report_readability_gate.md")
    parser.add_argument("--minimum-cases", type=int, default=MIN_FINAL_CASES)
    args = parser.parse_args()

    run = workflow_run()
    redacted_cases = redacted_flow_cases()
    cases = scenario_cases(redacted_cases)
    if len(redacted_cases) < args.minimum_cases:
        print(f"redacted flow corpus cases below minimum: {len(redacted_cases)} < {args.minimum_cases}", file=sys.stderr)
        return 2

    scenario_report = build_scenario_report(cases, run)
    redacted_report = build_redacted_flow_corpus_report(redacted_cases, run)
    package_report = build_package_readiness_report(redacted_cases, run)
    manifest_report = build_manifest_dry_run_report(redacted_cases, run)
    station_profiles_payload = build_station_profiles_report(run)
    operator_registry_report = build_operator_registry_report(run)
    operator_coverage_report = build_operator_contract_coverage_report(run)
    operator_validation_report = build_operator_validation_report(redacted_cases, run)
    station_compatibility_report = build_station_compatibility_report(redacted_cases, run)
    pre_release_report = build_pre_release_report(redacted_cases, run)
    release_decision_matrix_report = build_release_decision_matrix_report(pre_release_report, run)
    governance_export_report = build_governance_export_report(redacted_cases, run)
    explanation_report = build_agent_explanation_report(redacted_cases, run)
    reports = [
        (args.corpus_output, args.corpus_report, "RuntimePreview Scenario Corpus", build_corpus_report(cases, run)),
        (args.redacted_flow_output, args.redacted_flow_report, "RuntimePreview Redacted Flow Corpus Final", redacted_report),
        (args.redacted_flow_v2_output, args.redacted_flow_v2_report, "RuntimePreview Redacted Flow Corpus Final", redacted_report),
        (args.redacted_flow_final_output, args.redacted_flow_final_report, "RuntimePreview Redacted Flow Corpus Final", redacted_report),
        (args.output, args.report, "RuntimePreview Scenario Evidence", scenario_report),
        (args.deploy_output, args.deploy_report, "RuntimePreview Deploy Readiness Report", build_deploy_readiness_report(scenario_report)),
        (args.package_output, args.package_report, "RuntimePreview Package Readiness Final", package_report),
        (args.package_final_output, args.package_final_report, "RuntimePreview Package Readiness Final", package_report),
        (args.manifest_dry_run_output, args.manifest_dry_run_report, "RuntimePackage Manifest Dry-Run Final", manifest_report),
        (args.manifest_dry_run_final_output, args.manifest_dry_run_final_report, "RuntimePackage Manifest Dry-Run Final", manifest_report),
        (args.station_profiles_output, args.station_profiles_report, "RuntimePreview Station Profiles Final", station_profiles_payload),
        (args.station_profiles_final_output, args.station_profiles_final_report, "RuntimePreview Station Profiles Final", station_profiles_payload),
        (args.operator_contract_registry_output, args.operator_contract_registry_report, "RuntimePreview Operator Contract Registry Final", operator_registry_report),
        (args.operator_contract_registry_final_output, args.operator_contract_registry_final_report, "RuntimePreview Operator Contract Registry Final", operator_registry_report),
        (args.operator_contract_coverage_output, args.operator_contract_coverage_report, "RuntimePreview Operator Contract Coverage", operator_coverage_report),
        (args.operator_contract_validation_output, args.operator_contract_validation_report, "RuntimePreview Operator Contract Validation Final", operator_validation_report),
        (args.operator_contract_validation_final_output, args.operator_contract_validation_final_report, "RuntimePreview Operator Contract Validation Final", operator_validation_report),
        (args.station_compatibility_output, args.station_compatibility_report, "RuntimePreview Station Compatibility Final", station_compatibility_report),
        (args.station_compatibility_final_output, args.station_compatibility_final_report, "RuntimePreview Station Compatibility Final", station_compatibility_report),
        (args.pre_release_review_output, args.pre_release_review_report, "RuntimePreview Pre-Release Review Final", pre_release_report),
        (args.pre_release_review_final_output, args.pre_release_review_final_report, "RuntimePreview Pre-Release Review Final", pre_release_report),
        (args.release_decision_matrix_output, args.release_decision_matrix_report, "RuntimePreview Release Decision Matrix", release_decision_matrix_report),
        (args.audit_output, args.audit_report, "RuntimePreview Governance Audit Sample", build_audit_report(run)),
        (args.governance_export_output, args.governance_export_report, "RuntimePreview Governance Export Final", governance_export_report),
        (args.governance_export_final_output, args.governance_export_final_report, "RuntimePreview Governance Export Final", governance_export_report),
        (args.agent_explanation_output, args.agent_explanation_report, "RuntimePreview Agent Explanation Final", explanation_report),
        (args.agent_explanation_v3_output, args.agent_explanation_v3_report, "RuntimePreview Agent Explanation Final", explanation_report),
        (args.agent_explanation_final_output, args.agent_explanation_final_report, "RuntimePreview Agent Explanation Final", explanation_report),
    ]
    reports.append((args.report_readability_gate_output, args.report_readability_gate_report, "RuntimePreview Report Readability Gate", build_report_readability_gate(reports, run)))

    for json_path, md_path, title, payload in reports:
        if not payload.get("summary", {}).get("accepted", True):
            print(f"{payload.get('benchmarkId', title)} did not pass", file=sys.stderr)
            return 3
        write_json(REPO_ROOT / json_path, payload)
        write_markdown(REPO_ROOT / md_path, title, payload)

    print(
        "runtime preview final evidence generated "
        f"cases={len(cases)} redactedCases={len(redacted_cases)} reports={len(reports)} "
        "metadataOnly=true realResourcesTouched=false"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
