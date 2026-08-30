// CreateProjectCommand.cs
// CreateProject命令
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using MediatR;
using System.Text.Json;

namespace ClearVision.Product.Application.Commands.Projects;

public record CreateProjectCommand(string Name, string Description) : IRequest<Guid>;

public class CreateProjectCommandHandler : IRequestHandler<CreateProjectCommand, Guid>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly ProjectSaveCoordinator _saveCoordinator;
    private readonly IProjectAssetStorage? _projectAssetStorage;

    public CreateProjectCommandHandler(
        ProjectSaveCoordinator saveCoordinator,
        IProjectAssetStorage? projectAssetStorage = null)
    {
        _saveCoordinator = saveCoordinator ?? throw new ArgumentNullException(nameof(saveCoordinator));
        _projectAssetStorage = projectAssetStorage;
    }

    public async Task<Guid> Handle(CreateProjectCommand request, CancellationToken cancellationToken)
    {
        var project = new Project(request.Name, request.Description);
        var flow = new OperatorFlowDto
        {
            Id = project.Flow.Id,
            Name = project.Flow.Name,
            Operators = [],
            Connections = []
        };
        var saved = await _saveCoordinator.CreateProjectAsync(
            new ProjectCreateSaveRequest(
                project,
                flow,
                JsonSerializer.Serialize(flow, JsonOptions),
                _projectAssetStorage == null ? null : new ProjectAssetsDto()),
            cancellationToken);
        return saved.Project.Id;
    }
}
