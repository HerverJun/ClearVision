using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Desktop.Services;
using Microsoft.AspNetCore.Http;

namespace ClearVision.Product.Desktop.Endpoints;

internal static class AppConfigEndpointResults
{
    public const string ExpectedRevisionRequired = "APP_CONFIG_EXPECTED_REVISION_REQUIRED";

    public static IResult ReadFailure(
        AppConfigReadResult read,
        Func<AppConfig, object>? projectLastGood = null)
    {
        var lastGood = read.Config == null
            ? null
            : projectLastGood?.Invoke(read.Config) ?? read.Config;
        return Results.Json(new
        {
            success = false,
            errorCode = read.ErrorCode,
            message = read.Message,
            configStatus = read.Status.ToString(),
            degraded = true,
            hasLastGood = read.HasLastGood,
            revision = read.Config?.Revision,
            lastGood
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    public static IResult ExpectedRevisionFailure()
    {
        return Results.Json(new
        {
            success = false,
            errorCode = ExpectedRevisionRequired,
            message = "expectedRevision is required.",
            errors = new[]
            {
                new AppConfigValidationError("expectedRevision", "expectedRevision is required.")
            }
        }, statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    public static IResult MutationFailure(AppConfigMutationResult mutation)
    {
        var statusCode = mutation.Status switch
        {
            AppConfigMutationStatus.RevisionConflict => StatusCodes.Status409Conflict,
            AppConfigMutationStatus.ValidationFailed => StatusCodes.Status422UnprocessableEntity,
            AppConfigMutationStatus.ApplyFailed => StatusCodes.Status500InternalServerError,
            AppConfigMutationStatus.StorageFailure or AppConfigMutationStatus.Fenced =>
                StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Json(new
        {
            success = false,
            errorCode = mutation.ErrorCode,
            message = mutation.Message,
            expectedRevision = mutation.ExpectedRevision,
            actualRevision = mutation.ActualRevision,
            revision = mutation.ActualRevision,
            configStatus = mutation.Status.ToString(),
            degraded = mutation.Status is AppConfigMutationStatus.StorageFailure or AppConfigMutationStatus.Fenced,
            hasLastGood = mutation.Config != null,
            errors = mutation.ValidationErrors ?? Array.Empty<AppConfigValidationError>()
        }, statusCode: statusCode);
    }

    public static IResult CameraFailure(CameraConfigurationResult result)
    {
        if (result.Mutation != null)
        {
            return MutationFailure(result.Mutation);
        }

        var statusCode = result.ErrorCode switch
        {
            CameraConfigurationCoordinator.ErrorRuntimeConflict => StatusCodes.Status409Conflict,
            CameraConfigurationCoordinator.ErrorValidation or ExpectedRevisionRequired =>
                StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status503ServiceUnavailable
        };
        return Results.Json(new
        {
            success = false,
            errorCode = result.ErrorCode,
            message = result.Message,
            revision = result.Revision,
            configStatus = result.ReadStatus?.ToString()
                ?? (result.ErrorCode == CameraConfigurationCoordinator.ErrorRuntimeConflict
                    ? "RuntimeConflict"
                    : "ValidationFailed"),
            degraded = result.ReadStatus.HasValue,
            hasLastGood = result.HasLastGood,
            errors = result.ValidationErrors,
            activeStreams = result.RuntimeConflicts
        }, statusCode: statusCode);
    }
}
