# Phase 2.1: Implement Project CQRS Handlers - Execution Plan

## Objective
Migrate Project management logic from `ProjectService` to MediatR Command/Query handlers to enforce CQRS patterns and decouple the architecture.

## Diagnosis
- **Current Status**: `Commands` and `Queries` folders are empty. Logic resides in `ProjectService`.
- **Target**: Implement full CQRS for Project Aggregate.

## Execution Steps

### Step 1: Create Directory Structure
- `Acme.Product.Application/Commands/Projects`
- `Acme.Product.Application/Queries/Projects`

### Step 2: Implement Create Project
- **Command**: `CreateProjectCommand` (IRequest<Guid>)
  - Properties: `Name`, `Description`
- **Handler**: `CreateProjectCommandHandler`
  - Inject: `IProjectRepository`, `IUnitOfWork` (if applicable), `AutoMapper`.
  - Logic: Map DTO -> Entity, Repository.Add, Save.

### Step 3: Implement Update Flow
- **Command**: `UpdateFlowCommand` (IRequest<Unit>)
  - Properties: `ProjectId`, `Flow` (OperatorFlowDto)
- **Handler**: `UpdateFlowCommandHandler`
  - Logic: Fetch Project, Update Flow properties, Save.

### Step 4: Implement Get Project
- **Query**: `GetProjectQuery` (IRequest<ProjectDto>)
  - Properties: `Id`
- **Handler**: `GetProjectQueryHandler`
  - Logic: Repository.GetById, Map -> DTO.

### Step 5: Implement List Projects
- **Query**: `ListProjectsQuery` (IRequest<List<ProjectDto>>)
  - Properties: `SearchTerm` (optional)
- **Handler**: `ListProjectsQueryHandler`
  - Logic: Repository.List, Map -> List<DTO>.

### Step 6: Update Dependency Injection
- Ensure MediatR is scanning the assembly (already configured in `DependencyInjection`).
- Verify AutoMapper profiles cover `Project` <-> `ProjectDto`.

## Outcome
- Project operations are accessible via `_mediator.Send()`.
- `ProjectService` can be deprecated or refactored to use Mediator.
