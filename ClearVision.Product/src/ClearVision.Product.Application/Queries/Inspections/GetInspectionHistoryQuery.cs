// GetInspectionHistoryQuery.cs
// GetInspectionHistory查询
// 作者：蘅芜君

using System.Text.Json;
using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Interfaces;
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
        return new InspectionResultDto
        {
            Id = item.Id,
            ProjectId = item.ProjectId,
            Status = item.Status,
            Defects = item.Defects.Select(ToDto).ToList(),
            ProcessingTimeMs = item.ProcessingTimeMs,
            ImageId = item.ImageId,
            ConfidenceScore = item.ConfidenceScore,
            ErrorMessage = item.ErrorMessage,
            InspectionTime = item.InspectionTime,
            OutputImage = null,
            OutputData = TryDeserializeOutputData(item.OutputDataJson),
            AnalysisData = TryDeserializeAnalysisData(item.AnalysisDataJson)
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

    private static Dictionary<string, object>? TryDeserializeOutputData(string? json)
    {
        try
        {
            return AnalysisPayloadSerialization.DeserializeJsonDictionary(json);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static AnalysisDataDto? TryDeserializeAnalysisData(string? json)
    {
        try
        {
            return AnalysisPayloadSerialization.DeserializeAnalysisData(json);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
