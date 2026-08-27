using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Desktop.Configuration;
using ClearVision.Product.Desktop.Observation;
using ClearVision.Product.Desktop.PreviewArtifacts;
using ClearVision.Product.Infrastructure.Calibration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Operator = ClearVision.Product.Core.Entities.Operator;

namespace ClearVision.Product.Desktop.Endpoints;

public static class CalibrationDraftEndpoints
{
    public const string NPointWorkbenchFeatureFlag = "Studio:NPointCalibrationWorkbenchEnabled";
    private const int MaxDraftSamples = 512;
    private const int MaxDiagnostics = 64;
    private const int MaxArtifactBytes = 512 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapCalibrationDraftEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/calibration/npoint-draft/solve", (
            NPointCalibrationDraftSolveRequest request,
            HttpContext context,
            PreviewArtifactStore artifactStore,
            IOptions<StudioOptions> studioOptions,
            CancellationToken cancellationToken) =>
        {
            if (!studioOptions.Value.NPointCalibrationWorkbenchEnabled)
            {
                return Results.NotFound(new
                {
                    error = "NPoint calibration draft workbench is disabled.",
                    featureFlag = NPointWorkbenchFeatureFlag
                });
            }

            var response = SolveDraft(
                request,
                artifactStore,
                cancellationToken,
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty);
            return Results.Ok(response);
        });

        app.MapPost("/api/projects/{projectId:guid}/calibration-assets/from-draft", async (
            Guid projectId,
            NPointCalibrationFormalSaveRequest request,
            ProjectService projectService,
            IInspectionRuntimeCoordinator runtimeCoordinator,
            IOptions<StudioOptions> studioOptions,
            CancellationToken cancellationToken) =>
        {
            if (!studioOptions.Value.NPointCalibrationWorkbenchEnabled)
            {
                return Results.NotFound(new
                {
                    error = "NPoint calibration draft workbench is disabled.",
                    featureFlag = NPointWorkbenchFeatureFlag
                });
            }

            if (!TryBuildFormalSavePayload(request, out var payload, out var version, out var error))
            {
                return Results.BadRequest(new { Code = "PSV025", Error = error });
            }

            await using var mutationLease = await runtimeCoordinator.TryAcquireMutationLeaseAsync(
                projectId,
                "calibration-asset-save",
                cancellationToken);
            if (mutationLease == null)
            {
                return Results.Conflict(new { Code = "GV031", Error = "Project is currently running." });
            }

            try
            {
                var response = await projectService.SaveCalibrationAssetAsync(
                    projectId,
                    new ProjectCalibrationAssetSaveRequest
                    {
                        ExpectedPersistenceRevision = request.ExpectedPersistenceRevision,
                        AssetId = request.AssetId,
                        Version = version,
                        Producer = "NPointCalibrationDraftWorkbench",
                        SourceDraftSessionId = request.SessionId,
                        TargetNodeId = request.TargetNodeId,
                        ImageIdentity = request.ImageIdentity,
                        ExpectedContentHash = request.ExpectedContentHash,
                        Payload = payload
                    });

                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return ToProjectAssetFailure(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        return app;
    }

    internal static NPointCalibrationDraftSolveResponse SolveDraft(
        NPointCalibrationDraftSolveRequest request,
        PreviewArtifactStore artifactStore,
        CancellationToken cancellationToken = default,
        string artifactOwnerUserId = "")
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessionId = NormalizeIdentity(request.SessionId, "calibration-draft");
        var responseSamples = NormalizeSamples(request.Samples);
        var diagnostics = new List<string>();
        var mode = ResolveMode(request.Mode, diagnostics);
        var requiredCount = mode == NPointCalibrationMode.Perspective ? 4 : 3;
        var unit = NormalizeUnit(request.Unit);
        var options = ResolveOptions(request.SolverOptions, unit, requiredCount);

        var activePairs = new List<NPointCalibrationPointPair>();
        var activeSampleIndexes = new List<int>();
        for (var index = 0; index < responseSamples.Count; index++)
        {
            var sample = responseSamples[index];
            if (!sample.Enabled)
            {
                continue;
            }

            if (!sample.Valid)
            {
                diagnostics.Add($"Sample {sample.SampleId} is invalid and was excluded from the solver.");
                continue;
            }

            activePairs.Add(new NPointCalibrationPointPair(
                new Position(sample.PixelX!.Value, sample.PixelY!.Value),
                new Position(sample.WorldX!.Value, sample.WorldY!.Value)));
            activeSampleIndexes.Add(index);
        }

        var solver = new NPointCalibrationSolver();
        var solve = solver.Solve(new NPointCalibrationRequest(mode, activePairs, options));
        CalibrationBundleV2? candidateBundle = null;
        string? candidateBundleJson = null;
        NPointCalibrationDraftSolveResultDto? solveResult = null;

        if (!solve.Success)
        {
            diagnostics.Add(solve.ErrorMessage);
        }
        else
        {
            candidateBundle = solve.Bundle;
            candidateBundleJson = CalibrationBundleV2Json.Serialize(candidateBundle);
            solveResult = BuildSolveResult(solve);
            ApplySampleSolveResult(responseSamples, activeSampleIndexes, activePairs, solve);
        }

        var status = solve.Success ? "Solved" : "Failed";
        var draft = new NPointCalibrationDraftObservationDto
        {
            SchemaVersion = "calibration-draft-session.v1",
            SessionId = sessionId,
            ProjectId = request.ProjectId,
            TargetNodeId = request.TargetNodeId,
            ImageIdentity = request.ImageIdentity ?? string.Empty,
            Mode = mode.ToString(),
            Unit = unit,
            SampleCount = responseSamples.Count,
            EnabledCount = responseSamples.Count(sample => sample.Enabled),
            SolverSampleCount = activePairs.Count,
            Status = status,
            Dirty = true,
            DraftOnly = true,
            NotSavedToProjectAssets = true,
            Samples = responseSamples,
            LastSolveResult = solveResult,
            CandidateBundle = candidateBundle,
            CandidateBundleJson = candidateBundleJson,
            Diagnostics = diagnostics.Take(MaxDiagnostics).ToList()
        };

        var artifacts = MaterializeDraftArtifacts(
            request,
            sessionId,
            draft,
            candidateBundleJson,
            artifactStore,
            cancellationToken,
            diagnostics,
            artifactOwnerUserId);
        draft.Artifacts = artifacts;

        return new NPointCalibrationDraftSolveResponse
        {
            SchemaVersion = "calibration-draft-session.v1",
            SessionId = sessionId,
            ProjectId = request.ProjectId,
            TargetNodeId = request.TargetNodeId,
            ImageIdentity = request.ImageIdentity ?? string.Empty,
            Mode = draft.Mode,
            Unit = unit,
            Status = status,
            Success = solve.Success,
            ErrorMessage = solve.Success ? null : solve.ErrorMessage,
            DraftOnly = true,
            NotSavedToProjectAssets = true,
            Samples = responseSamples,
            LastSolveResult = solveResult,
            CandidateBundle = candidateBundle,
            CandidateBundleJson = candidateBundleJson,
            Artifacts = artifacts,
            Diagnostics = diagnostics.Take(MaxDiagnostics).ToList(),
            Observation = BuildObservation(request, draft, solve.Success, solve.ErrorMessage)
        };
    }

    private static bool TryBuildFormalSavePayload(
        NPointCalibrationFormalSaveRequest request,
        out JsonElement payload,
        out string? version,
        out string error)
    {
        payload = default;
        version = null;
        error = string.Empty;
        CalibrationBundleV2 bundle;
        if (!string.IsNullOrWhiteSpace(request.CandidateBundleJson))
        {
            if (!CalibrationBundleV2Json.TryDeserialize(request.CandidateBundleJson, out bundle, out error))
            {
                return false;
            }
        }
        else if (request.CandidateBundle != null)
        {
            bundle = request.CandidateBundle;
            if (!CalibrationBundleV2Json.TryValidateBase(bundle, out error))
            {
                return false;
            }
        }
        else
        {
            error = "Calibration candidate bundle is required.";
            return false;
        }

        if (!CalibrationBundleV2Json.TryRequireAccepted(bundle, out error))
        {
            return false;
        }

        payload = JsonSerializer.SerializeToElement(bundle, CalibrationBundleV2Json.DefaultOptions);
        version = string.IsNullOrWhiteSpace(bundle.CalibrationVersion)
            ? null
            : bundle.CalibrationVersion.Trim();
        return true;
    }

    private static IResult ToProjectAssetFailure(Exception ex)
    {
        if (TryParseStableError(ex.Message, out var code, out var message))
        {
            return string.Equals(code, "PSV011", StringComparison.Ordinal)
                ? Results.Conflict(new { Code = code, Error = message })
                : Results.BadRequest(new { Code = code, Error = message });
        }

        return Results.BadRequest(new { Error = ex.Message });
    }

    private static bool TryParseStableError(string? message, out string code, out string error)
    {
        code = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var separatorIndex = message.IndexOf(':', StringComparison.Ordinal);
        var candidateCode = separatorIndex > 0 ? message[..separatorIndex] : message;
        if (!candidateCode.StartsWith("GV", StringComparison.OrdinalIgnoreCase) &&
            !candidateCode.StartsWith("PSV", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        code = candidateCode;
        error = separatorIndex >= 0
            ? message[(separatorIndex + 1)..].TrimStart()
            : message;
        return true;
    }

    private static NPointCalibrationDraftSolveResultDto BuildSolveResult(NPointCalibrationResult solve)
    {
        var stats = solve.ErrorStats;
        return new NPointCalibrationDraftSolveResultDto
        {
            Success = true,
            TransformModel = solve.TransformModel.ToString(),
            Matrix = solve.TransformMatrix,
            MeanError = stats.MeanError,
            MaxError = stats.MaxError,
            InlierMeanError = stats.InlierMeanError,
            InlierMaxError = stats.InlierMaxError,
            AllSampleMeanError = stats.AllSampleMeanError,
            AllSampleMaxError = stats.AllSampleMaxError,
            InlierCount = stats.InlierCount,
            TotalSampleCount = stats.InlierCount == 0 && solve.InlierFlags.Count == 0 ? 0 : solve.InlierFlags.Count,
            InlierRatio = stats.InlierRatio,
            Accepted = solve.Bundle.Quality.Accepted,
            Diagnostics = solve.Bundle.Quality.Diagnostics.Take(MaxDiagnostics).ToList()
        };
    }

    private static void ApplySampleSolveResult(
        IReadOnlyList<NPointCalibrationDraftSampleResultDto> samples,
        IReadOnlyList<int> activeSampleIndexes,
        IReadOnlyList<NPointCalibrationPointPair> activePairs,
        NPointCalibrationResult solve)
    {
        for (var activeIndex = 0; activeIndex < activeSampleIndexes.Count; activeIndex++)
        {
            var sample = samples[activeSampleIndexes[activeIndex]];
            var pair = activePairs[activeIndex];
            var projected = TryProjectWorldToPixel(solve, pair.WorldPoint, out var imageX, out var imageY);
            sample.Inlier = activeIndex < solve.InlierFlags.Count ? solve.InlierFlags[activeIndex] : null;
            if (!projected)
            {
                continue;
            }

            sample.ReprojectionX = imageX;
            sample.ReprojectionY = imageY;
            var dx = imageX - pair.ImagePoint.X;
            var dy = imageY - pair.ImagePoint.Y;
            sample.Error = Math.Sqrt((dx * dx) + (dy * dy));
        }
    }

    private static bool TryProjectWorldToPixel(NPointCalibrationResult solve, Position world, out double imageX, out double imageY)
    {
        imageX = 0;
        imageY = 0;
        if (solve.TransformModel == TransformModelV2.Affine &&
            TryInvertAffine(solve.TransformMatrix, out var affine))
        {
            imageX = affine[0][0] * world.X + affine[0][1] * world.Y + affine[0][2];
            imageY = affine[1][0] * world.X + affine[1][1] * world.Y + affine[1][2];
            return double.IsFinite(imageX) && double.IsFinite(imageY);
        }

        if (solve.TransformModel == TransformModelV2.Homography &&
            TryInvert3x3(solve.TransformMatrix, out var homography))
        {
            var w = homography[2][0] * world.X + homography[2][1] * world.Y + homography[2][2];
            if (!double.IsFinite(w) || Math.Abs(w) <= 1e-12)
            {
                return false;
            }

            imageX = (homography[0][0] * world.X + homography[0][1] * world.Y + homography[0][2]) / w;
            imageY = (homography[1][0] * world.X + homography[1][1] * world.Y + homography[1][2]) / w;
            return double.IsFinite(imageX) && double.IsFinite(imageY);
        }

        return false;
    }

    private static bool TryInvertAffine(IReadOnlyList<double[]> matrix, out double[][] inverse)
    {
        inverse = Array.Empty<double[]>();
        if (matrix.Count != 2 || matrix[0].Length != 3 || matrix[1].Length != 3)
        {
            return false;
        }

        var a = matrix[0][0];
        var b = matrix[0][1];
        var c = matrix[0][2];
        var d = matrix[1][0];
        var e = matrix[1][1];
        var f = matrix[1][2];
        var det = (a * e) - (b * d);
        if (!double.IsFinite(det) || Math.Abs(det) <= 1e-12)
        {
            return false;
        }

        inverse =
        [
            [e / det, -b / det, ((b * f) - (e * c)) / det],
            [-d / det, a / det, ((d * c) - (a * f)) / det]
        ];
        return true;
    }

    private static bool TryInvert3x3(IReadOnlyList<double[]> matrix, out double[][] inverse)
    {
        inverse = Array.Empty<double[]>();
        if (matrix.Count != 3 || matrix.Any(row => row.Length != 3))
        {
            return false;
        }

        var a = matrix[0][0];
        var b = matrix[0][1];
        var c = matrix[0][2];
        var d = matrix[1][0];
        var e = matrix[1][1];
        var f = matrix[1][2];
        var g = matrix[2][0];
        var h = matrix[2][1];
        var i = matrix[2][2];
        var det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        if (!double.IsFinite(det) || Math.Abs(det) <= 1e-12)
        {
            return false;
        }

        inverse =
        [
            [(e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det],
            [(f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det],
            [(d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det]
        ];
        return true;
    }

    private static List<PreviewArtifactReferenceV1> MaterializeDraftArtifacts(
        NPointCalibrationDraftSolveRequest request,
        string sessionId,
        NPointCalibrationDraftObservationDto draft,
        string? candidateBundleJson,
        PreviewArtifactStore artifactStore,
        CancellationToken cancellationToken,
        List<string> diagnostics,
        string artifactOwnerUserId)
    {
        var owner = new PreviewArtifactOwnerScope(
            request.ProjectId,
            request.TargetNodeId,
            request.DebugSessionId ?? Guid.Empty,
            request.ClientRequestSequence,
            request.FlowRevision,
            artifactOwnerUserId);
        using var batch = artifactStore.CreateBatch(owner);
        var artifacts = new List<PreviewArtifactReferenceV1>();
        TryAddJsonArtifact(
            batch,
            artifacts,
            diagnostics,
            "calibrationDraft",
            "calibration-draft-session.v1",
            "$.CalibrationDraft",
            new
            {
                draft.SchemaVersion,
                draft.SessionId,
                draft.ProjectId,
                draft.TargetNodeId,
                draft.ImageIdentity,
                draft.Mode,
                draft.Unit,
                draft.SampleCount,
                draft.EnabledCount,
                draft.SolverSampleCount,
                draft.Status,
                draft.DraftOnly,
                draft.NotSavedToProjectAssets,
                draft.LastSolveResult,
                draft.Diagnostics
            },
            cancellationToken);
        TryAddJsonArtifact(
            batch,
            artifacts,
            diagnostics,
            "calibrationSampleTable",
            "calibration-sample-table.v1",
            "$.CalibrationDraft.Samples",
            draft.Samples,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(candidateBundleJson))
        {
            TryAddJsonArtifact(
                batch,
                artifacts,
                diagnostics,
                "calibrationCandidateBundle",
                "calibration-candidate-bundle.v1",
                "$.CalibrationDraft.CandidateBundle",
                JsonSerializer.Deserialize<JsonElement>(candidateBundleJson),
                cancellationToken);
        }

        batch.Commit();
        return artifacts;
    }

    private static void TryAddJsonArtifact(
        PreviewArtifactBatch batch,
        List<PreviewArtifactReferenceV1> artifacts,
        List<string> diagnostics,
        string kind,
        string role,
        string pathHint,
        object value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (bytes.Length > MaxArtifactBytes)
        {
            diagnostics.Add($"{role} artifact omitted because {bytes.Length.ToString(CultureInfo.InvariantCulture)} bytes exceeds {MaxArtifactBytes.ToString(CultureInfo.InvariantCulture)}.");
            return;
        }

        try
        {
            artifacts.Add(batch.Add(kind, role, pathHint, "application/json", bytes));
        }
        catch (PreviewArtifactStoreRejectedException ex)
        {
            diagnostics.Add($"{role} artifact rejected: {ex.Message}");
        }
    }

    private static ExecutionObservationEnvelopeV1 BuildObservation(
        NPointCalibrationDraftSolveRequest request,
        NPointCalibrationDraftObservationDto draft,
        bool success,
        string errorMessage)
    {
        var draftElement = JsonSerializer.SerializeToElement(draft, JsonOptions);
        var outputData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["CalibrationDraft"] = draftElement,
            ["SampleTable"] = draft.Samples,
            ["CandidateBundle"] = draft.CandidateBundle is null
                ? new { draftOnly = true, status = draft.Status }
                : (object)draft.CandidateBundle
        };

        var targetOperator = new Operator(request.TargetNodeId, "N Point Calibration Draft", OperatorType.NPointCalibration, 0, 0);
        targetOperator.LoadOutputPort(CreateStableGuid(request.TargetNodeId, "CalibrationDraft"), "CalibrationDraft", PortDataType.String);
        targetOperator.LoadOutputPort(CreateStableGuid(request.TargetNodeId, "SampleTable"), "SampleTable", PortDataType.String);
        targetOperator.LoadOutputPort(CreateStableGuid(request.TargetNodeId, "CandidateBundle"), "CandidateBundle", PortDataType.String);

        return ExecutionObservationProjector.CreatePreviewObservation(new ExecutionObservationPreviewInput
        {
            ProjectId = request.ProjectId,
            TargetNodeId = request.TargetNodeId,
            DebugSessionId = request.DebugSessionId ?? Guid.Empty,
            ClientRequestSequence = request.ClientRequestSequence,
            FlowRevision = request.FlowRevision,
            Success = success,
            ErrorMessage = success ? null : errorMessage,
            ExecutedOperatorCount = 1,
            OutputData = outputData,
            OutputPorts = targetOperator.OutputPorts
                .Select(port => new ExecutionObservationOutputPortV1
                {
                    Id = port.Id,
                    Name = port.Name,
                    DataType = port.DataType
                })
                .ToList(),
            TargetOperator = targetOperator,
            FeatureFlags = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [NPointWorkbenchFeatureFlag] = true
            }
        });
    }

    private static Guid CreateStableGuid(Guid scope, string name)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"{scope:D}:{name}"));
        return new Guid(bytes);
    }

    private static NPointCalibrationMode ResolveMode(string? raw, List<string> diagnostics)
    {
        if (string.Equals(raw, "Perspective", StringComparison.OrdinalIgnoreCase))
        {
            return NPointCalibrationMode.Perspective;
        }

        if (!string.Equals(raw, "Affine", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add("Calibration mode was missing or invalid; Affine was used.");
        }

        return NPointCalibrationMode.Affine;
    }

    private static string NormalizeUnit(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? "mm" : raw.Trim();

    private static string NormalizeIdentity(string? raw, string fallback) =>
        string.IsNullOrWhiteSpace(raw) ? $"{fallback}-{Guid.NewGuid():N}" : raw.Trim();

    private static NPointCalibrationOptions ResolveOptions(
        NPointCalibrationDraftSolverOptionsDto? raw,
        string unit,
        int requiredCount)
    {
        raw ??= new NPointCalibrationDraftSolverOptionsDto();
        var minInlierCount = raw.MinInlierCount <= 0 ? requiredCount : raw.MinInlierCount;
        return new NPointCalibrationOptions(
            FiniteOr(raw.RansacReprojectionThreshold, 3.0),
            Math.Clamp(raw.RansacMaxIterations <= 0 ? 3000 : raw.RansacMaxIterations, 1, 100000),
            Math.Clamp(FiniteOr(raw.RansacConfidence, 0.995), 0.001, 0.999999),
            Math.Max(0, FiniteOr(raw.MaxAcceptedReprojectionError, 3.0)),
            Math.Max(0, minInlierCount),
            Math.Clamp(FiniteOr(raw.MinInlierRatio, 0.5), 0.0, 1.0),
            unit,
            "NPointCalibrationDraftWorkbench");
    }

    private static double FiniteOr(double value, double fallback) =>
        double.IsFinite(value) ? value : fallback;

    private static List<NPointCalibrationDraftSampleResultDto> NormalizeSamples(IEnumerable<NPointCalibrationDraftSampleDto>? samples)
    {
        return (samples ?? [])
            .Take(MaxDraftSamples)
            .Select((sample, index) =>
            {
                var result = new NPointCalibrationDraftSampleResultDto
                {
                    SampleId = NormalizeIdentity(sample.SampleId, $"sample-{index + 1}"),
                    Order = sample.Order > 0 ? sample.Order : index + 1,
                    PixelX = FiniteOrNull(sample.PixelX),
                    PixelY = FiniteOrNull(sample.PixelY),
                    WorldX = FiniteOrNull(sample.WorldX),
                    WorldY = FiniteOrNull(sample.WorldY),
                    Source = string.IsNullOrWhiteSpace(sample.Source) ? "ManualClick" : sample.Source.Trim(),
                    Enabled = sample.Enabled,
                    Note = sample.Note ?? string.Empty,
                    CreatedAtUtc = sample.CreatedAtUtc ?? DateTimeOffset.UtcNow
                };
                result.Valid =
                    result.PixelX.HasValue &&
                    result.PixelY.HasValue &&
                    result.WorldX.HasValue &&
                    result.WorldY.HasValue &&
                    double.IsFinite(result.PixelX.Value) &&
                    double.IsFinite(result.PixelY.Value) &&
                    double.IsFinite(result.WorldX.Value) &&
                    double.IsFinite(result.WorldY.Value);
                if (!result.Valid)
                {
                    result.ValidationMessage = "Pixel and world coordinates must be finite.";
                }

                return result;
            })
            .OrderBy(sample => sample.Order)
            .ThenBy(sample => sample.SampleId, StringComparer.Ordinal)
            .ToList();
    }

    private static double? FiniteOrNull(double? value) =>
        value.HasValue && double.IsFinite(value.Value) ? value.Value : null;
}

public sealed class NPointCalibrationDraftSolveRequest
{
    public string? SessionId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid TargetNodeId { get; init; }
    public Guid? DebugSessionId { get; init; }
    public long? ClientRequestSequence { get; init; }
    public long? FlowRevision { get; init; }
    public string? ImageIdentity { get; init; }
    public string Mode { get; init; } = "Affine";
    public string Unit { get; init; } = "mm";
    public NPointCalibrationDraftSolverOptionsDto SolverOptions { get; init; } = new();
    public List<NPointCalibrationDraftSampleDto> Samples { get; init; } = [];
}

public sealed class NPointCalibrationFormalSaveRequest
{
    public long? ExpectedPersistenceRevision { get; init; }

    public string? AssetId { get; init; }

    public string? SessionId { get; init; }

    public Guid? TargetNodeId { get; init; }

    public string? ImageIdentity { get; init; }

    public string? CandidateBundleJson { get; init; }

    public CalibrationBundleV2? CandidateBundle { get; init; }

    public string? ExpectedContentHash { get; init; }
}

public sealed class NPointCalibrationDraftSolverOptionsDto
{
    public double RansacReprojectionThreshold { get; init; } = 3.0;
    public int RansacMaxIterations { get; init; } = 3000;
    public double RansacConfidence { get; init; } = 0.995;
    public double MaxAcceptedReprojectionError { get; init; } = 3.0;
    public int MinInlierCount { get; init; }
    public double MinInlierRatio { get; init; } = 0.5;
}

public sealed class NPointCalibrationDraftSampleDto
{
    public string? SampleId { get; init; }
    public int Order { get; init; }
    public double? PixelX { get; init; }
    public double? PixelY { get; init; }
    public double? WorldX { get; init; }
    public double? WorldY { get; init; }
    public string Source { get; init; } = "ManualClick";
    public bool Enabled { get; init; } = true;
    public string? Note { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed class NPointCalibrationDraftSampleResultDto
{
    public string SampleId { get; init; } = string.Empty;
    public int Order { get; init; }
    public double? PixelX { get; init; }
    public double? PixelY { get; init; }
    public double? WorldX { get; init; }
    public double? WorldY { get; init; }
    public string Source { get; init; } = "ManualClick";
    public bool Enabled { get; init; } = true;
    public bool Valid { get; set; }
    public string? ValidationMessage { get; set; }
    public bool? Inlier { get; set; }
    public double? ReprojectionX { get; set; }
    public double? ReprojectionY { get; set; }
    public double? Error { get; set; }
    public string Note { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class NPointCalibrationDraftSolveResultDto
{
    public bool Success { get; init; }
    public string TransformModel { get; init; } = string.Empty;
    public double[][] Matrix { get; init; } = [];
    public double MeanError { get; init; }
    public double MaxError { get; init; }
    public double InlierMeanError { get; init; }
    public double InlierMaxError { get; init; }
    public double AllSampleMeanError { get; init; }
    public double AllSampleMaxError { get; init; }
    public int InlierCount { get; init; }
    public int TotalSampleCount { get; init; }
    public double InlierRatio { get; init; }
    public bool Accepted { get; init; }
    public List<string> Diagnostics { get; init; } = [];
}

public sealed class NPointCalibrationDraftObservationDto
{
    public string SchemaVersion { get; init; } = "calibration-draft-session.v1";
    public string SessionId { get; init; } = string.Empty;
    public Guid ProjectId { get; init; }
    public Guid TargetNodeId { get; init; }
    public string ImageIdentity { get; init; } = string.Empty;
    public string Mode { get; init; } = "Affine";
    public string Unit { get; init; } = "mm";
    public int SampleCount { get; init; }
    public int EnabledCount { get; init; }
    public int SolverSampleCount { get; init; }
    public string Status { get; init; } = "Draft";
    public bool Dirty { get; init; }
    public bool DraftOnly { get; init; } = true;
    public bool NotSavedToProjectAssets { get; init; } = true;
    public List<NPointCalibrationDraftSampleResultDto> Samples { get; init; } = [];
    public NPointCalibrationDraftSolveResultDto? LastSolveResult { get; init; }
    public CalibrationBundleV2? CandidateBundle { get; init; }
    public string? CandidateBundleJson { get; init; }
    public List<string> Diagnostics { get; init; } = [];
    public List<PreviewArtifactReferenceV1> Artifacts { get; set; } = [];
}

public sealed class NPointCalibrationDraftSolveResponse
{
    public string SchemaVersion { get; init; } = "calibration-draft-session.v1";
    public string SessionId { get; init; } = string.Empty;
    public Guid ProjectId { get; init; }
    public Guid TargetNodeId { get; init; }
    public string ImageIdentity { get; init; } = string.Empty;
    public string Mode { get; init; } = "Affine";
    public string Unit { get; init; } = "mm";
    public string Status { get; init; } = "Draft";
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool DraftOnly { get; init; } = true;
    public bool NotSavedToProjectAssets { get; init; } = true;
    public List<NPointCalibrationDraftSampleResultDto> Samples { get; init; } = [];
    public NPointCalibrationDraftSolveResultDto? LastSolveResult { get; init; }
    public CalibrationBundleV2? CandidateBundle { get; init; }
    public string? CandidateBundleJson { get; init; }
    public List<PreviewArtifactReferenceV1> Artifacts { get; init; } = [];
    public List<string> Diagnostics { get; init; } = [];
    public ExecutionObservationEnvelopeV1? Observation { get; init; }
}
