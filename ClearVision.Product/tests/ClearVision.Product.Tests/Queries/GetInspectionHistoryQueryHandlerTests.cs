using ClearVision.Product.Application.Queries.Inspections;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using FluentAssertions;
using NSubstitute;

namespace ClearVision.Product.Tests.Queries;

public sealed class GetInspectionHistoryQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldUseLightweightHistoryPageAndOmitOutputImage()
    {
        var repository = Substitute.For<IInspectionResultRepository>();
        var projectId = Guid.NewGuid();
        var startTime = DateTime.UtcNow.AddHours(-1);
        var endTime = DateTime.UtcNow;
        var imageId = Guid.NewGuid();
        var resultId = Guid.NewGuid();

        repository.GetHistoryPageAsync(
                projectId,
                startTime,
                endTime,
                null,
                null,
                0,
                50)
            .Returns(new InspectionHistoryPage
            {
                Items =
                [
                    new InspectionHistoryItem
                    {
                        Id = resultId,
                        ProjectId = projectId,
                        Status = InspectionStatus.NG,
                        ProcessingTimeMs = 42,
                        ImageId = imageId,
                        ConfidenceScore = 0.87,
                        ErrorMessage = "threshold failed",
                        InspectionTime = endTime,
                        OutputDataJson = """{"score":42}""",
                        AnalysisDataJson = """{"version":1,"cards":[]}""",
                        Defects =
                        [
                            new InspectionHistoryDefectItem
                            {
                                Id = Guid.NewGuid(),
                                Type = DefectType.Scratch,
                                X = 1,
                                Y = 2,
                                Width = 3,
                                Height = 4,
                                ConfidenceScore = 0.9,
                                Description = "scratch"
                            }
                        ]
                    }
                ],
                TotalCount = 1,
                PageIndex = 0,
                PageSize = 50
            });

        var handler = new GetInspectionHistoryQueryHandler(repository);

        var results = await handler.Handle(
            new GetInspectionHistoryQuery(projectId, startTime, endTime, 50),
            CancellationToken.None);

        var dto = results.Should().ContainSingle().Subject;
        dto.Id.Should().Be(resultId);
        dto.ProjectId.Should().Be(projectId);
        dto.Status.Should().Be(InspectionStatus.NG);
        dto.ImageId.Should().Be(imageId);
        dto.OutputImage.Should().BeNull();
        dto.OutputData.Should().ContainKey("score");
        dto.AnalysisData.Should().NotBeNull();
        dto.Defects.Should().ContainSingle();

        _ = repository.Received(1).GetHistoryPageAsync(projectId, startTime, endTime, null, null, 0, 50);
        _ = repository.DidNotReceive().GetByProjectIdAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>());
        _ = repository.DidNotReceive().GetByTimeRangeAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            Arg.Any<string?>(),
            Arg.Any<string?>());
    }
}
