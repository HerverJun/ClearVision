using ClearVision.Product.Desktop.PreviewArtifacts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClearVision.Product.Desktop.Endpoints;

public static class PreviewArtifactEndpoints
{
    public static IEndpointRouteBuilder MapPreviewArtifactEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/preview-artifacts/{artifactId}", (
            string artifactId,
            HttpContext context,
            PreviewArtifactStore artifactStore) =>
        {
            if (!artifactStore.TryRead(artifactId, out var artifact) || artifact == null)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ETag = $"\"sha256-{artifact.Sha256}\"";
            context.Response.Headers["X-Artifact-Sha256"] = artifact.Sha256;
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.ContentLength = artifact.Length;

            return Results.File(
                artifact.Bytes,
                artifact.ContentType,
                enableRangeProcessing: false);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        app.MapDelete("/api/preview-artifacts/{artifactId}", (
            string artifactId,
            PreviewArtifactStore artifactStore) =>
        {
            if (!PreviewArtifactStore.IsValidArtifactId(artifactId))
            {
                return Results.NotFound();
            }

            return artifactStore.Delete(artifactId)
                ? Results.NoContent()
                : Results.NotFound();
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        return app;
    }
}
