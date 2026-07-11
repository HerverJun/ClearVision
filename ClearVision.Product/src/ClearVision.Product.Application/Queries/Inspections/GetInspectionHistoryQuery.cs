// GetInspectionHistoryQuery.cs
// GetInspectionHistory查询
// 作者：蘅芜君

using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Outcomes;
using MediatR;

namespace ClearVision.Product.Application.Queries.Inspections;

public record GetInspectionHistoryQuery(Guid ProjectId, DateTime? StartDate, DateTime? EndDate, int Limit = 100) : IRequest<List<InspectionResultDto>>;

public class GetInspectionHistoryQueryHandler : IRequestHandler<GetInspectionHistoryQuery, List<InspectionResultDto>>
{
    private readonly IInspectionResultRepository _repository;

    public GetInspectionHistoryQueryHandler(IInspectionResultRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<InspectionResultDto>> Handle(GetInspectionHistoryQuery request, CancellationToken cancellationToken)
    {
        var pageSize = request.Limit > 0 ? request.Limit : 200;
        var page = await _repository.GetHistoryPageAsync(
            request.ProjectId,
            request.StartDate,
            request.EndDate,
            pageIndex: 0,
            pageSize: pageSize);

        return page.Items.Select(ToDto).ToList();
    }

    private static InspectionResultDto ToDto(InspectionHistoryItem item)
    {
        var outcome = item.ExecutionOutcome.HasValue && item.DecisionOutcome.HasValue
            ? new InspectionOutcome(
                item.ExecutionOutcome.Value,
                item.DecisionOutcome.Value,
                item.DecisionSource,
                item.ReasonCode,
                item.ErrorMessage)
            : LegacyInspectionStatusProjection.FromLegacy(item.Status) with { Message = item.ErrorMessage };

        return new InspectionResultDto
        {
            Id = item.Id,
            ProjectId = item.ProjectId,
            Status = item.Status,
            ExecutionOutcome = outcome.Execution,
            DecisionOutcome = outcome.Decision,
            DecisionSource = outcome.DecisionSource,
            ReasonCode = outcome.ReasonCode,
            Defects = item.Defects.Select(ToDto).ToList(),
            ProcessingTimeMs = item.ProcessingTimeMs,
            ImageId = item.ImageId,
            ConfidenceScore = item.ConfidenceScore,
            ErrorMessage = item.ErrorMessage,
            InspectionTime = item.InspectionTime,
            OutputImage = null,
            OutputData = null,
            AnalysisData = null
        };
    }

    private static DefectDto ToDto(InspectionHistoryDefectItem defect)
    {
        return new DefectDto
        {
            Id = defect.Id,
            Type = defect.Type,
            X = defect.X,
            Y = defect.Y,
            Width = defect.Width,
            Height = defect.Height,
            ConfidenceScore = defect.ConfidenceScore,
            Description = defect.Description,
            AnnotationData = defect.AnnotationData
        };
    }

}
