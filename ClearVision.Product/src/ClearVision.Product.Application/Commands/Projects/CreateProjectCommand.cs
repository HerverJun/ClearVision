// CreateProjectCommand.cs
// CreateProject命令
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using MediatR;

namespace ClearVision.Product.Application.Commands.Projects;

public record CreateProjectCommand(string Name, string Description) : IRequest<Guid>;

public class CreateProjectCommandHandler : IRequestHandler<CreateProjectCommand, Guid>
{
    private readonly IProjectRepository _repository;

    public CreateProjectCommandHandler(IProjectRepository repository)
    {
        _repository = repository;
    }

    public async Task<Guid> Handle(CreateProjectCommand request, CancellationToken cancellationToken)
    {
        var project = new Project(request.Name, request.Description);
        await _repository.AddAsync(project);
        return project.Id;
    }
}
