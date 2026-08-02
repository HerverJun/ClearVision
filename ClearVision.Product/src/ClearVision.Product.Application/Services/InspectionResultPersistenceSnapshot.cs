using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Application.Services;

public static class InspectionResultPersistenceSnapshot
{
    public static InspectionResult WithoutOutputImage(InspectionResult source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var snapshot = new InspectionResult(source.ProjectId, source.ImageId);
        if (source.ExecutionOutcome.HasValue && source.DecisionOutcome.HasValue)
        {
            snapshot.SetOutcome(source.GetOutcome(), source.ProcessingTimeMs, source.ConfidenceScore);
        }
        else
        {
            snapshot.RestoreLegacyResult(
                source.Status,
                source.ProcessingTimeMs,
                source.ConfidenceScore,
                source.ErrorMessage);
        }

        if (!string.IsNullOrWhiteSpace(source.OutputDataJson))
        {
            snapshot.SetOutputDataJson(source.OutputDataJson);
        }

        if (!string.IsNullOrWhiteSpace(source.AnalysisDataJson))
        {
            snapshot.SetAnalysisDataJson(source.AnalysisDataJson);
        }

        snapshot.CopyTraceabilityFrom(source);

        foreach (var defect in source.Defects)
        {
            snapshot.AddDefect(new Defect(
                source.Id,
                defect.Type,
                defect.X,
                defect.Y,
                defect.Width,
                defect.Height,
                defect.ConfidenceScore,
                defect.Description,
                defect.AnnotationData));
        }

        snapshot.RestorePersistenceMetadata(
            source.Id,
            source.InspectionTime,
            source.CreatedAt,
            source.ModifiedAt);
        return snapshot;
    }
}
