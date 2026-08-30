namespace ClearVision.Product.Application.Services;

/// <summary>
/// Builds the public result-scoped image reference. The cache identifier is deliberately
/// absent because database Project/InspectionResult authority is re-evaluated on every read.
/// </summary>
public static class InspectionResultImageReferenceBuilder
{
    public static string? Build(
        Guid projectId,
        Guid resultId,
        Guid? imageId)
    {
        return imageId.HasValue &&
               projectId != Guid.Empty &&
               resultId != Guid.Empty
            ? $"/api/projects/{projectId:D}/inspection-results/{resultId:D}/image"
            : null;
    }
}
