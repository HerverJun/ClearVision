
namespace ClearVision.Product.Tests.AI.AgentEvaluation;

internal static class AgentEngineeringEvaluationCases
{
    private const string Source = AgentEvaluationHarness.MockSource;

    public static IReadOnlyList<AgentEngineeringEvaluationCase> All { get; } =
    [
        WireSequenceFlowGeneration(),
        TemplateMatchingFlowGeneration(),
        HoleDistanceMeasurementFlowGeneration(),
        MissingCameraBinding(),
        MissingModelPath(),
        StationOffline(),
        MultipleImageAcquisitionRequiresEntry(),
        RuntimePreviewDeniesCaptureByDefault(),
        RuntimePreviewAuthorizedReplay(),
        PrecheckBlocksCameraFlowWithoutReplay()
    ];

    private static AgentEngineeringEvaluationCase WireSequenceFlowGeneration()
    {
        var flow = Flow(
        [
            Op("op_cam", "ImageAcquisition", ("CameraBindingId", "mock-cam-wire")),
            Op("op_roi", "RoiManager", ("RoiName", "terminal_strip")),
            Op("op_detect", "DeepLearning", ("ModelPath", "mock://models/wire-sequence-yolo.onnx")),
            Op("op_judge", "ResultJudgment", ("Rule", "wire_order_matches_expected")),
            Op("op_out", "ResultOutput", ("Channel", "qa"))
        ],
        [
            Link("op_cam", "Image", "op_roi", "Image"),
            Link("op_roi", "RoiImage", "op_detect", "Image"),
            Link("op_detect", "Detections", "op_judge", "Input"),
            Link("op_judge", "Result", "op_out", "Input")
        ]);

        return new AgentEngineeringEvaluationCase
        {
            CaseId = "wire_sequence_flow_generation",
            UserRequest = "Create an engineering draft for terminal wire sequence inspection.",
            Flow = flow,
            MockToolResponses =
            [
                ReadOnly("match_flow_template", TemplateMatch("wire_sequence_inspection")),
                ReadOnly("get_flow_template_skeleton", Skeleton("ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput")),
                ReadOnly("list_camera_bindings", CameraBindings("mock-cam-wire")),
                ReadOnly("get_operator_schema", Schema("DeepLearning")),
                Simulation("validate_flow", Validation(valid: true)),
                Simulation("dryrun_flow", DryRun(succeeded: true)),
                Precheck(PrecheckPayload(
                    ready: true,
                    warnings: ["Camera flow has no successful replay_flow_with_frame result."]),
                    [Pending("runtimePackagePrecheck.review", "Review runtime package precheck")])
            ],
            ToolCalls =
            [
                EvaluationToolCall.Create("match_flow_template", Args(("scenario", "wire_sequence"))),
                EvaluationToolCall.Create("get_flow_template_skeleton", Args(("templateId", "wire_sequence_inspection"))),
                EvaluationToolCall.Create("list_camera_bindings"),
                EvaluationToolCall.Create("get_operator_schema", Args(("operatorType", "DeepLearning"))),
                EvaluationToolCall.WithFlow("validate_flow"),
                EvaluationToolCall.WithFlow("dryrun_flow"),
                EvaluationToolCall.WithFlow("runtime_package_precheck", Args(
                    ("targetStationId", "mock-station-line"),
                    ("requireReplayForCameraFlow", false)),
                    includeDryRunSummary: true,
                    includeReplaySummary: true)
            ],
            ExpectedToolCalls =
            [
                "match_flow_template",
                "get_flow_template_skeleton",
                "list_camera_bindings",
                "get_operator_schema",
                "validate_flow",
                "dryrun_flow",
                "runtime_package_precheck"
            ],
            ExpectedFlowStructure = Structure(
                ["ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"],
                connectionCount: 4,
                imageAcquisitionCount: 1),
            ExpectedPendingActions = ["runtimePackagePrecheck.review"],
            ExpectedValidationPreview = Preview("ok", "not_run", "ready_with_warnings",
                "dryrun_flow", "runtime_package_precheck"),
            ExpectedPermissionBehavior = Permission(
                runtimePreviewAllowed: false,
                denied: [],
                runtimePreviewExecuted: [],
                deploymentPrepareExecuted: ["runtime_package_precheck"]),
            ExpectedBlockingIssues = [],
            ExpectedPassFailReason = "Pass: mock wire sequence flow is structurally valid and records replay as a precheck warning."
        };
    }

    private static AgentEngineeringEvaluationCase TemplateMatchingFlowGeneration()
    {
        var flow = Flow(
        [
            Op("op_cam", "ImageAcquisition", ("CameraBindingId", "mock-cam-template")),
            Op("op_match", "TemplateMatching", ("TemplatePath", "mock://templates/bracket-a.template")),
            Op("op_correct", "PositionCorrection", ("Mode", "match_pose")),
            Op("op_judge", "ResultJudgment", ("MinScore", "0.82")),
            Op("op_out", "ResultOutput", ("Channel", "plc"))
        ],
        [
            Link("op_cam", "Image", "op_match", "Image"),
            Link("op_match", "Pose", "op_correct", "Pose"),
            Link("op_match", "Score", "op_judge", "Input"),
            Link("op_judge", "Result", "op_out", "Input")
        ]);

        return new AgentEngineeringEvaluationCase
        {
            CaseId = "template_matching_flow_generation",
            UserRequest = "Generate a template matching inspection flow for bracket alignment.",
            Flow = flow,
            MockToolResponses =
            [
                ReadOnly("match_flow_template", TemplateMatch("template_matching_alignment")),
                ReadOnly("get_flow_template_skeleton", Skeleton("ImageAcquisition", "TemplateMatching", "PositionCorrection", "ResultJudgment", "ResultOutput")),
                ReadOnly("list_camera_bindings", CameraBindings("mock-cam-template")),
                ReadOnly("get_operator_schema", Schema("TemplateMatching")),
                Simulation("validate_flow", Validation(valid: true)),
                Simulation("dryrun_flow", DryRun(succeeded: true)),
                Precheck(PrecheckPayload(
                    ready: true,
                    warnings: ["Camera flow has no successful replay_flow_with_frame result."]),
                    [Pending("runtimePackagePrecheck.review", "Review runtime package precheck")])
            ],
            ToolCalls =
            [
                EvaluationToolCall.Create("match_flow_template", Args(("scenario", "template_matching"))),
                EvaluationToolCall.Create("get_flow_template_skeleton", Args(("templateId", "template_matching_alignment"))),
                EvaluationToolCall.Create("list_camera_bindings"),
                EvaluationToolCall.Create("get_operator_schema", Args(("operatorType", "TemplateMatching"))),
                EvaluationToolCall.WithFlow("validate_flow"),
                EvaluationToolCall.WithFlow("dryrun_flow"),
                EvaluationToolCall.WithFlow("runtime_package_precheck", Args(
                    ("targetStationId", "mock-station-template"),
                    ("requireReplayForCameraFlow", false)),
                    includeDryRunSummary: true,
                    includeReplaySummary: true)
            ],
            ExpectedToolCalls =
            [
                "match_flow_template",
                "get_flow_template_skeleton",
                "list_camera_bindings",
                "get_operator_schema",
                "validate_flow",
                "dryrun_flow",
                "runtime_package_precheck"
            ],
            ExpectedFlowStructure = Structure(
                ["ImageAcquisition", "TemplateMatching", "PositionCorrection", "ResultJudgment", "ResultOutput"],
                connectionCount: 4,
                imageAcquisitionCount: 1),
            ExpectedPendingActions = ["runtimePackagePrecheck.review"],
            ExpectedValidationPreview = Preview("ok", "not_run", "ready_with_warnings",
                "dryrun_flow", "runtime_package_precheck"),
            ExpectedPermissionBehavior = Permission(
                runtimePreviewAllowed: false,
                denied: [],
                runtimePreviewExecuted: [],
                deploymentPrepareExecuted: ["runtime_package_precheck"]),
            ExpectedBlockingIssues = [],
            ExpectedPassFailReason = "Pass: template matching draft uses mock template resources and keeps real replay unclaimed."
        };
    }

    private static AgentEngineeringEvaluationCase HoleDistanceMeasurementFlowGeneration()
    {
        var flow = Flow(
        [
            Op("op_cam", "ImageAcquisition", ("CameraBindingId", "mock-cam-hole")),
            Op("op_circle_a", "CircleMeasurement", ("Roi", "hole_a")),
            Op("op_circle_b", "CircleMeasurement", ("Roi", "hole_b")),
            Op("op_distance", "MeasureDistance", ("Unit", "mm")),
            Op("op_judge", "ResultJudgment", ("Tolerance", "+/-0.05")),
            Op("op_out", "ResultOutput", ("Channel", "mes"))
        ],
        [
            Link("op_cam", "Image", "op_circle_a", "Image"),
            Link("op_cam", "Image", "op_circle_b", "Image"),
            Link("op_circle_a", "Center", "op_distance", "PointA"),
            Link("op_circle_b", "Center", "op_distance", "PointB"),
            Link("op_distance", "Distance", "op_judge", "Input"),
            Link("op_judge", "Result", "op_out", "Input")
        ]);

        return new AgentEngineeringEvaluationCase
        {
            CaseId = "hole_distance_measurement_flow_generation",
            UserRequest = "Generate a no-image hole distance measurement flow.",
            Flow = flow,
            MockToolResponses =
            [
                ReadOnly("retrieve_operator_knowledge", Knowledge("measurement chain requires calibration review")),
                ReadOnly("list_camera_bindings", CameraBindings("mock-cam-hole")),
                ReadOnly("get_operator_schema", Schema("CircleMeasurement")),
                ReadOnly("get_operator_schema", Schema("MeasureDistance")),
                Simulation("validate_flow", Validation(valid: true)),
                Simulation("dryrun_flow", DryRun(succeeded: true)),
                Precheck(PrecheckPayload(
                    ready: true,
                    warnings:
                    [
                        "Camera flow has no successful replay_flow_with_frame result.",
                        "Measurement calibration must be reviewed before station deployment."
                    ]),
                    [Pending("runtimePackagePrecheck.review", "Review runtime package precheck")])
            ],
            ToolCalls =
            [
                EvaluationToolCall.Create("retrieve_operator_knowledge", Args(("keyword", "hole distance measurement"))),
                EvaluationToolCall.Create("list_camera_bindings"),
                EvaluationToolCall.Create("get_operator_schema", Args(("operatorType", "CircleMeasurement"))),
                EvaluationToolCall.Create("get_operator_schema", Args(("operatorType", "MeasureDistance"))),
                EvaluationToolCall.WithFlow("validate_flow"),
                EvaluationToolCall.WithFlow("dryrun_flow"),
                EvaluationToolCall.WithFlow("runtime_package_precheck", Args(
                    ("targetStationId", "mock-station-hole"),
                    ("requireReplayForCameraFlow", false)),
                    includeDryRunSummary: true,
                    includeReplaySummary: true)
            ],
            ExpectedToolCalls =
            [
                "retrieve_operator_knowledge",
                "list_camera_bindings",
                "get_operator_schema",
                "get_operator_schema",
                "validate_flow",
                "dryrun_flow",
                "runtime_package_precheck"
            ],
            ExpectedFlowStructure = Structure(
                ["ImageAcquisition", "CircleMeasurement", "CircleMeasurement", "MeasureDistance", "ResultJudgment", "ResultOutput"],
                connectionCount: 6,
                imageAcquisitionCount: 1),
            ExpectedPendingActions = ["runtimePackagePrecheck.review"],
            ExpectedValidationPreview = Preview("ok", "not_run", "ready_with_warnings",
                "dryrun_flow", "runtime_package_precheck"),
            ExpectedPermissionBehavior = Permission(
                runtimePreviewAllowed: false,
                denied: [],
                runtimePreviewExecuted: [],
                deploymentPrepareExecuted: ["runtime_package_precheck"]),
            ExpectedBlockingIssues = [],
            ExpectedPassFailReason = "Pass: hole-distance flow remains a structure-only engineering draft with calibration review surfaced."
        };
    }

    private static AgentEngineeringEvaluationCase MissingCameraBinding()
    {
        var flow = Flow(
        [
            Op("op_cam", "ImageAcquisition", ("CameraBindingId", "")),
            Op("op_match", "TemplateMatching", ("TemplatePath", "mock://templates/part.template")),
            Op("op_out", "ResultOutput")
        ],
        [
            Link("op_cam", "Image", "op_match", "Image"),
            Link("op_match", "Result", "op_out", "Input")
        ],
        [
            Missing("camera_binding", "op_cam.CameraBindingId", "ImageAcquisition must be bound to a configured camera.")
        ]);

        const string missingBinding = "op_cam ImageAcquisition is missing CameraBindingId.";
        return new AgentEngineeringEvaluationCase
        {
            CaseId = "missing_camera_binding",
            UserRequest = "Generate a flow, but no camera binding is known yet.",
            Flow = flow,
            MockToolResponses =
            [
                ReadOnly("list_camera_bindings", new { source = Source, activeCameraId = (string?)null, bindings = Array.Empty<object>() }),
                Simulation("validate_flow", Validation(valid: true, warnings: ["CameraBindingId is pending engineer input."])),
                Simulation("dryrun_flow", DryRun(succeeded: false, blockingIssues: [missingBinding])),
                Precheck(PrecheckPayload(
                    ready: false,
                    blockingIssues: [missingBinding],
                    warnings: ["No camera bindings are configured."],
                    requiredUserActions: ["Bind a camera for ImageAcquisition op_cam."]),
                    [
                        Pending("cameraBinding.required", "Bind camera for op_cam"),
                        Pending("runtimePackagePrecheck.review", "Review runtime package precheck")
                    ])
            ],
            ToolCalls =
            [
                EvaluationToolCall.Create("list_camera_bindings"),
                EvaluationToolCall.WithFlow("validate_flow"),
                EvaluationToolCall.WithFlow("dryrun_flow"),
                EvaluationToolCall.WithFlow("runtime_package_precheck", Args(
                    ("targetStationId", "mock-station-missing-camera"),
                    ("requireReplayForCameraFlow", true)),
                    includeDryRunSummary: true,
                    includeReplaySummary: true)
            ],
            ExpectedToolCalls =
            [
                "list_camera_bindings",
                "validate_flow",
                "dryrun_flow",
                "runtime_package_precheck"
            ],
            ExpectedFlowStructure = Structure(
                ["ImageAcquisition", "TemplateMatching", "ResultOutput"],
                connectionCount: 2,
                imageAcquisitionCount: 1,
                missingResources: ["camera_binding:op_cam.CameraBindingId"]),
            ExpectedPendingActions = ["cameraBinding.required", "runtimePackagePrecheck.review"],
            ExpectedValidationPreview = Preview("blocked", "not_run", "blocked",
                "dryrun_flow", "runtime_package_precheck"),
            ExpectedPermissionBehavior = Permission(
                runtimePreviewAllowed: false,
                denied: [],
                runtimePreviewExecuted: [],
                deploymentPrepareExecuted: ["runtime_package_precheck"]),
            ExpectedBlockingIssues = [missingBinding],
            ExpectedPassFailReason = "Pass: missing camera binding is carried into pending actions and deployment blocking issues."
        };
    }

    private static AgentEngineeringEvaluationCase MissingModelPath()
    {
        var flow = Flow(
        [
            Op("op_cam", "ImageAcquisition", ("CameraBindingId", "mock-cam-model")),
            Op("op_detect", "DeepLearning", ("ModelPath", "")),
            Op("op_out", "ResultOutput")
        ],
        [
            Link("op_cam", "Image", "op_detect", "Image"),
            Link("op_detect", "Detections", "op_out", "Input")
        ],
        [
            Missing("model_path", "op_detect.ModelPath", "DeepLearning requires a model path before runtime package preparation.")
        ]);

        const string missingModel = "op_detect.ModelPath is missing.";
        return new AgentEngineeringEvaluationCase
        {
            CaseId = "missing_model_path",
            UserRequest = "Create a defect detection flow but the model path has not been selected.",
            Flow = flow,
            MockToolResponses =
            [
                ReadOnly("retrieve_operator_knowledge", Knowledge("DeepLearning ModelPath is required for deployment.")),
                ReadOnly("list_camera_bindings", CameraBindings("mock-cam-model")),
                Simulation("validate_flow", Validation(valid: true, warnings: ["ModelPath is pending engineer input."])),
                Simulation("dryrun_flow", DryRun(succeeded: false, blockingIssues: [missingModel])),
                Precheck(PrecheckPayload(
                    ready: false,
                    blockingIssues: [missingModel],
                    warnings: ["Camera flow has no successful replay_flow_with_frame result."],
                    requiredUserActions: ["Provide model path for op_detect.ModelPath."]),
                    [
                        Pending("modelPath.required", "Select model path for op_detect"),
                        Pending("runtimePackagePrecheck.review", "Review runtime package precheck")
                    ])
            ],
            ToolCalls =
            [
                EvaluationToolCall.Create("retrieve_operator_knowledge", Args(("operatorType", "DeepLearning"))),
                EvaluationToolCall.Create("list_camera_bindings"),
                EvaluationToolCall.WithFlow("validate_flow"),
                EvaluationToolCall.WithFlow("dryrun_flow"),
                EvaluationToolCall.WithFlow("runtime_package_precheck", Args(
                    ("targetStationId", "mock-station-model"),
                    ("requireReplayForCameraFlow", false)),
                    includeDryRunSummary: true,
                    includeReplaySummary: true)
            ],
            ExpectedToolCalls =
            [
                "retrieve_operator_knowledge",
                "list_camera_bindings",
                "validate_flow",
                "dryrun_flow",
                "runtime_package_precheck"
            ],
            ExpectedFlowStructure = Structure(
                ["ImageAcquisition", "DeepLearning", "ResultOutput"],
                connectionCount: 2,
                imageAcquisitionCount: 1,
                missingResources: ["model_path:op_detect.ModelPath"]),
            ExpectedPendingActions = ["modelPath.required", "runtimePackagePrecheck.review"],
            ExpectedValidationPreview = Preview("blocked", "not_run", "blocked",
                "dryrun_flow", "runtime_package_precheck"),
            ExpectedPermissionBehavior = Permission(
                runtimePreviewAllowed: false,
                denied: [],
                runtimePreviewExecuted: [],
                deploymentPrepareExecuted: ["runtime_package_precheck"]),
            ExpectedBlockingIssues = [missingModel],
            ExpectedPassFailReason = "Pass: missing model path is exposed as a pending resource and blocks package readiness."
        };
    }

    private static AgentEngineeringEvaluationCase StationOffline()
    {
        var flow = Flow(
        [
            Op("op_cam", "ImageAcquisition", ("CameraBindingId", "mock-cam-station")),
            Op("op_match", "TemplateMatching", ("TemplatePath", "mock://templates/offline-station.template")),
            Op("op_out", "ResultOutput")
        ],
        [
            Link("op_cam", "Image", "op_match", "Image"),
            Link("op_match", "Result", "op_out", "Input")
        ]);

        const string offline = "Target Station 'mock-station-offline' is offline.";
        return new AgentEngineeringEvaluationCase
        {
            CaseId = "station_offline",
            UserRequest = "Prepare this camera flow for a station that is currently offline.",
            Flow = flow,
            MockToolResponses =
            [
                ReadOnly("check_station_status", new
                {
                    source = Source,
                    stations = new[] { new { stationId = "mock-station-offline", online = false } },
                    count = 1
                }),
                Simulation("validate_flow", Validation(valid: true)),
                Simulation("dryrun_flow", DryRun(succeeded: true)),
                Precheck(PrecheckPayload(
                    ready: false,
                    blockingIssues: [offline],
                    warnings: ["Camera flow has no successful replay_flow_with_frame result."],
                    requiredUserActions: ["Bring the target Station online before deployment."]),
                    [Pending("runtimePackagePrecheck.review", "Review runtime package precheck")])
            ],
            ToolCalls =
            [
                EvaluationToolCall.Create("check_station_status", Args(("targetStationId", "mock-station-offline"))),
                EvaluationToolCall.WithFlow("validate_flow"),
                EvaluationToolCall.WithFlow("dryrun_flow"),
                EvaluationToolCall.WithFlow("runtime_package_precheck", Args(
                    ("targetStationId", "mock-station-offline"),
                    ("requireReplayForCameraFlow", false)),
                    includeDryRunSummary: true,
                    includeReplaySummary: true)
            ],
            ExpectedToolCalls =
            [
                "check_station_status",
                "validate_flow",
                "dryrun_flow",
                "runtime_package_precheck"
            ],
            ExpectedFlowStructure = Structure(
                ["ImageAcquisition", "TemplateMatching", "ResultOutput"],
                connectionCount: 2,
                imageAcquisitionCount: 1),
            ExpectedPendingActions = ["runtimePackagePrecheck.review"],
            ExpectedValidationPreview = Preview("ok", "not_run", "blocked",
                "dryrun_flow", "runtime_package_precheck"),
            ExpectedPermissionBehavior = Permission(
                runtimePreviewAllowed: false,
                denied: [],
                runtimePreviewExecuted: [],
                deploymentPrepareExecuted: ["runtime_package_precheck"]),
            ExpectedBlockingIssues = [offline],
            ExpectedPassFailReason = "Pass: offline mock Station prevents deployment precheck from passing."
        };
    }

    private static AgentEngineeringEvaluationCase MultipleImageAcquisitionRequiresEntry()
    {
        var flow = Flow(
        [
            Op("op_cam_top", "ImageAcquisition", ("CameraBindingId", "mock-cam-top")),
            Op("op_cam_side", "ImageAcquisition", ("CameraBindingId", "mock-cam-side")),
            Op("op_join", "ImageCompose", ("Mode", "side_by_side")),
            Op("op_out", "ResultOutput")
        ],
        [
            Link("op_cam_top", "Image", "op_join", "ImageA"),
            Link("op_cam_side", "Image", "op_join", "ImageB"),
            Link("op_join", "Image", "op_out", "Input")
        ]);

        const string missingEntry = "Flow contains multiple ImageAcquisition operators. Provide entryOperatorTempId to select the exact entry.";
        return new AgentEngineeringEvaluationCase
        {
            CaseId = "multiple_image_acquisition_requires_entry",
            UserRequest = "Replay a two-camera flow without specifying the entry ImageAcquisition operator.",
            AllowRuntimePreview = true,
            Flow = flow,
            MockToolResponses =
            [
                Runtime("capture_test_frame", Capture("mock-frame-multi", "mock-cam-top")),
                MockToolResponse.Fail(
                    "replay_flow_with_frame",
                    "multiple_image_acquisition_requires_entry_operator_temp_id",
                    missingEntry,
                    ReplayBlocked(missingEntry),
                    AgentEvaluationToolPermission.RuntimePreview,
                    [Pending("entryOperatorTempId.required", "Select replay entry ImageAcquisition")])
            ],
            ToolCalls =
            [
                EvaluationToolCall.Create("capture_test_frame", Args(("cameraBindingId", "mock-cam-top"))),
                EvaluationToolCall.Replay()
            ],
            ExpectedToolCalls =
            [
                "capture_test_frame",
                "replay_flow_with_frame"
            ],
            ExpectedFlowStructure = Structure(
                ["ImageAcquisition", "ImageAcquisition", "ImageCompose", "ResultOutput"],
                connectionCount: 3,
                imageAcquisitionCount: 2),
            ExpectedPendingActions = ["entryOperatorTempId.required"],
            ExpectedValidationPreview = Preview("not_run", "blocked", "not_run",
                "replay_flow_with_frame"),
            ExpectedPermissionBehavior = Permission(
                runtimePreviewAllowed: true,
                denied: [],
                runtimePreviewExecuted: ["capture_test_frame", "replay_flow_with_frame"],
                deploymentPrepareExecuted: []),
            ExpectedBlockingIssues = [missingEntry, $"replay_flow_with_frame: {missingEntry}"],
            ExpectedPassFailReason = "Pass: multi-acquisition replay is blocked until entryOperatorTempId is selected."
        };
    }

    private static AgentEngineeringEvaluationCase RuntimePreviewDeniesCaptureByDefault()
    {
        var flow = Flow(
        [
            Op("op_cam", "ImageAcquisition", ("CameraBindingId", "mock-cam-default-deny")),
            Op("op_out", "ResultOutput")
        ],
        [
            Link("op_cam", "Image", "op_out", "Input")
        ]);

        return new AgentEngineeringEvaluationCase
        {
            CaseId = "runtime_preview_denies_capture_by_default",
            UserRequest = "Try to capture a test frame without explicit RuntimePreview authorization.",
            AllowRuntimePreview = false,
            Flow = flow,
            MockToolResponses =
            [
                ReadOnly("list_camera_bindings", CameraBindings("mock-cam-default-deny")),
                Runtime("capture_test_frame", Capture("mock-frame-denied", "mock-cam-default-deny"))
            ],
            ToolCalls =
            [
                EvaluationToolCall.Create("list_camera_bindings"),
                EvaluationToolCall.Create("capture_test_frame", Args(("cameraBindingId", "mock-cam-default-deny")))
            ],
            ExpectedToolCalls =
            [
                "list_camera_bindings",
                "capture_test_frame"
            ],
            ExpectedFlowStructure = Structure(
                ["ImageAcquisition", "ResultOutput"],
                connectionCount: 1,
                imageAcquisitionCount: 1),
            ExpectedPendingActions = [],
            ExpectedValidationPreview = Preview("not_run", "not_run", "not_run"),
            ExpectedPermissionBehavior = Permission(
                runtimePreviewAllowed: false,
                denied: ["capture_test_frame"],
                runtimePreviewExecuted: [],
                deploymentPrepareExecuted: []),
            ExpectedBlockingIssues = [],
            ExpectedPassFailReason = "Pass: RuntimePreview is disabled by default and capture_test_frame is denied before mock execution."
        };
    }

    private static AgentEngineeringEvaluationCase RuntimePreviewAuthorizedReplay()
    {
        var flow = Flow(
        [
            Op("op_cam", "ImageAcquisition", ("CameraBindingId", "mock-cam-authorized")),
            Op("op_match", "TemplateMatching", ("TemplatePath", "mock://templates/authorized.template")),
            Op("op_out", "ResultOutput")
        ],
        [
            Link("op_cam", "Image", "op_match", "Image"),
            Link("op_match", "Result", "op_out", "Input")
        ]);

        return new AgentEngineeringEvaluationCase
        {
            CaseId = "runtime_preview_authorized_replay",
            UserRequest = "User explicitly allows RuntimePreview; capture a mock frame and replay the flow.",
            AllowRuntimePreview = true,
            Flow = flow,
            MockToolResponses =
            [
                Runtime("capture_test_frame", Capture("mock-frame-authorized", "mock-cam-authorized")),
                Runtime("replay_flow_with_frame", ReplaySucceeded("mock-frame-authorized", "op_cam")),
                Precheck(PrecheckPayload(ready: true),
                    [Pending("runtimePackagePrecheck.review", "Review runtime package precheck")])
            ],
            ToolCalls =
            [
                EvaluationToolCall.Create("capture_test_frame", Args(("cameraBindingId", "mock-cam-authorized"))),
                EvaluationToolCall.Replay(Args(("entryOperatorTempId", "op_cam"))),
                EvaluationToolCall.WithFlow("runtime_package_precheck", Args(
                    ("targetStationId", "mock-station-authorized"),
                    ("requireReplayForCameraFlow", true)),
                    includeReplaySummary: true)
            ],
            ExpectedToolCalls =
            [
                "capture_test_frame",
                "replay_flow_with_frame",
                "runtime_package_precheck"
            ],
            ExpectedFlowStructure = Structure(
                ["ImageAcquisition", "TemplateMatching", "ResultOutput"],
                connectionCount: 2,
                imageAcquisitionCount: 1),
            ExpectedPendingActions = ["runtimePackagePrecheck.review"],
            ExpectedValidationPreview = Preview("not_run", "ok", "ready",
                "replay_flow_with_frame", "runtime_package_precheck"),
            ExpectedPermissionBehavior = Permission(
                runtimePreviewAllowed: true,
                denied: [],
                runtimePreviewExecuted: ["capture_test_frame", "replay_flow_with_frame"],
                deploymentPrepareExecuted: ["runtime_package_precheck"]),
            ExpectedBlockingIssues = [],
            ExpectedPassFailReason = "Pass: explicit RuntimePreview authorization allows mock capture, replay, and ready precheck."
        };
    }

    private static AgentEngineeringEvaluationCase PrecheckBlocksCameraFlowWithoutReplay()
    {
        var flow = Flow(
        [
            Op("op_cam", "ImageAcquisition", ("CameraBindingId", "mock-cam-precheck")),
            Op("op_match", "TemplateMatching", ("TemplatePath", "mock://templates/precheck.template")),
            Op("op_out", "ResultOutput")
        ],
        [
            Link("op_cam", "Image", "op_match", "Image"),
            Link("op_match", "Result", "op_out", "Input")
        ]);

        const string replayRequired = "Camera flow requires a successful replay_flow_with_frame result before deployment precheck can pass.";
        return new AgentEngineeringEvaluationCase
        {
            CaseId = "precheck_blocks_camera_flow_without_replay",
            UserRequest = "Run strict runtime package precheck for a camera flow that has not been replayed.",
            Flow = flow,
            MockToolResponses =
            [
                Simulation("validate_flow", Validation(valid: true)),
                Simulation("dryrun_flow", DryRun(succeeded: true)),
                Precheck(PrecheckPayload(
                    ready: false,
                    blockingIssues: [replayRequired],
                    requiredUserActions: ["Run capture_test_frame and replay_flow_with_frame successfully for the selected camera entry."]),
                    [Pending("runtimePackagePrecheck.review", "Review runtime package precheck")])
            ],
            ToolCalls =
            [
                EvaluationToolCall.WithFlow("validate_flow"),
                EvaluationToolCall.WithFlow("dryrun_flow"),
                EvaluationToolCall.WithFlow("runtime_package_precheck", Args(
                    ("targetStationId", "mock-station-precheck"),
                    ("requireReplayForCameraFlow", true)),
                    includeDryRunSummary: true,
                    includeReplaySummary: true)
            ],
            ExpectedToolCalls =
            [
                "validate_flow",
                "dryrun_flow",
                "runtime_package_precheck"
            ],
            ExpectedFlowStructure = Structure(
                ["ImageAcquisition", "TemplateMatching", "ResultOutput"],
                connectionCount: 2,
                imageAcquisitionCount: 1),
            ExpectedPendingActions = ["runtimePackagePrecheck.review"],
            ExpectedValidationPreview = Preview("ok", "not_run", "blocked",
                "dryrun_flow", "runtime_package_precheck"),
            ExpectedPermissionBehavior = Permission(
                runtimePreviewAllowed: false,
                denied: [],
                runtimePreviewExecuted: [],
                deploymentPrepareExecuted: ["runtime_package_precheck"]),
            ExpectedBlockingIssues = [replayRequired],
            ExpectedPassFailReason = "Pass: strict package precheck blocks camera deployment when replay is missing."
        };
    }

    private static MockToolResponse ReadOnly(string toolName, object data) =>
        MockToolResponse.Ok(toolName, data, AgentEvaluationToolPermission.ReadOnly);

    private static MockToolResponse Simulation(string toolName, object data) =>
        MockToolResponse.Ok(toolName, data, AgentEvaluationToolPermission.Simulation);

    private static MockToolResponse Runtime(string toolName, object data) =>
        MockToolResponse.Ok(toolName, data, AgentEvaluationToolPermission.RuntimePreview);

    private static MockToolResponse Precheck(object data, IReadOnlyList<AgentEvaluationPendingAction> pendingActions) =>
        MockToolResponse.Ok(
            "runtime_package_precheck",
            data,
            AgentEvaluationToolPermission.DeploymentPrepare,
            requiresUserConfirmation: true,
            pendingActions: pendingActions);

    private static object TemplateMatch(string templateId) => new
    {
        source = Source,
        matchedTemplateId = templateId,
        confidence = 0.95,
        note = "fixed mock template match"
    };

    private static object Skeleton(params string[] operators) => new
    {
        source = Source,
        operators,
        note = "fixed mock flow skeleton"
    };

    private static object CameraBindings(params string[] bindingIds) => new
    {
        source = Source,
        activeCameraId = bindingIds.FirstOrDefault(),
        bindings = bindingIds.Select(id => new { id, displayName = $"Mock {id}", isEnabled = true }).ToArray()
    };

    private static object Schema(string operatorType) => new
    {
        source = Source,
        operatorType,
        ports = new[] { "Input", "Output" },
        note = "mock schema only"
    };

    private static object Knowledge(string summary) => new
    {
        source = Source,
        summary,
        citations = Array.Empty<string>()
    };

    private static object Validation(
        bool valid,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<string>? warnings = null)
    {
        return new
        {
            source = Source,
            valid,
            errors = errors ?? [],
            warnings = warnings ?? [],
            diagnostics = Array.Empty<object>()
        };
    }

    private static object DryRun(
        bool succeeded,
        IReadOnlyList<string>? blockingIssues = null,
        IReadOnlyList<string>? warnings = null)
    {
        return new
        {
            source = Source,
            dryRunExecuted = succeeded,
            valid = succeeded && (blockingIssues == null || blockingIssues.Count == 0),
            isValid = succeeded && (blockingIssues == null || blockingIssues.Count == 0),
            dryRunSucceeded = succeeded,
            blockingIssues = blockingIssues ?? [],
            warnings = warnings ?? [],
            note = "structure-only mock dryrun; no real image verification"
        };
    }

    private static object PrecheckPayload(
        bool ready,
        IReadOnlyList<string>? blockingIssues = null,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<string>? requiredUserActions = null)
    {
        return new
        {
            source = Source,
            ready,
            blockingIssues = blockingIssues ?? [],
            warnings = warnings ?? [],
            requiredUserActions = requiredUserActions ?? []
        };
    }

    private static object Capture(string temporaryFrameId, string cameraBindingId) => new
    {
        source = Source,
        temporaryFrameId,
        cameraBindingId,
        byteLength = 0,
        width = 0,
        height = 0,
        pixelFormat = "MockNoImage"
    };

    private static object ReplaySucceeded(string temporaryFrameId, string entryOperatorTempId) => new
    {
        source = Source,
        replayExecuted = true,
        replaySucceeded = true,
        replayKind = "mock_frame_runtime_preview",
        temporaryFrameId,
        usedEntryOperatorTempId = entryOperatorTempId,
        outputSummary = new { Result = "OK" }
    };

    private static object ReplayBlocked(string issue) => new
    {
        source = Source,
        replayExecuted = false,
        replaySucceeded = false,
        blockingIssues = new[] { issue }
    };

    private static AgentEvaluationPendingAction Pending(string actionType, string title) => new()
    {
        ActionType = actionType,
        Title = title,
        Summary = title,
        Payload = new { source = Source },
        RequiresUserConfirmation = true
    };

    private static EvaluationFlow Flow(
        IReadOnlyList<EvaluationOperator> operators,
        IReadOnlyList<EvaluationConnection> connections,
        IReadOnlyList<EvaluationMissingResource>? missingResources = null)
    {
        return new EvaluationFlow
        {
            Operators = operators,
            Connections = connections,
            MissingResources = missingResources ?? []
        };
    }

    private static EvaluationOperator Op(
        string tempId,
        string operatorType,
        params (string Key, string Value)[] parameters)
    {
        return new EvaluationOperator
        {
            TempId = tempId,
            OperatorType = operatorType,
            DisplayName = operatorType,
            Parameters = parameters.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static EvaluationConnection Link(
        string sourceTempId,
        string sourcePortName,
        string targetTempId,
        string targetPortName)
    {
        return new EvaluationConnection
        {
            SourceTempId = sourceTempId,
            SourcePortName = sourcePortName,
            TargetTempId = targetTempId,
            TargetPortName = targetPortName
        };
    }

    private static EvaluationMissingResource Missing(
        string resourceType,
        string resourceKey,
        string description)
    {
        return new EvaluationMissingResource
        {
            ResourceType = resourceType,
            ResourceKey = resourceKey,
            Description = description
        };
    }

    private static EvaluationFlowStructure Structure(
        IReadOnlyList<string> operatorTypes,
        int connectionCount,
        int imageAcquisitionCount,
        IReadOnlyList<string>? missingResources = null)
    {
        return new EvaluationFlowStructure
        {
            OperatorTypes = operatorTypes,
            ConnectionCount = connectionCount,
            ImageAcquisitionCount = imageAcquisitionCount,
            MissingResourceKeys = missingResources ?? []
        };
    }

    private static AgentEvaluationValidationPreview Preview(
        string dryRun,
        string replay,
        string precheck,
        params string[] trace)
    {
        return new AgentEvaluationValidationPreview
        {
            StructuralDryRunStatus = dryRun,
            FrameReplayStatus = replay,
            RuntimePackagePrecheckStatus = precheck,
            ToolDryRunTrace = trace
        };
    }

    private static AgentEvaluationPermissionDecision Permission(
        bool runtimePreviewAllowed,
        IReadOnlyList<string> denied,
        IReadOnlyList<string> runtimePreviewExecuted,
        IReadOnlyList<string> deploymentPrepareExecuted)
    {
        return new AgentEvaluationPermissionDecision
        {
            RuntimePreviewAllowed = runtimePreviewAllowed,
            DeniedToolNames = denied,
            RuntimePreviewExecutedToolNames = runtimePreviewExecuted,
            DeploymentPrepareExecutedToolNames = deploymentPrepareExecuted
        };
    }

    private static IReadOnlyDictionary<string, object?> Args(params (string Key, object? Value)[] items)
    {
        return items.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);
    }
}
