# Phase 2.2: Implement Inspection CQRS Handlers - Execution Plan

## Objective
Migrate Inspection execution and history logic to CQRS handlers.

## Diagnosis
- **Current Status**: Logic in `InspectionService`.
- **Target**: CQRS for high-throughput inspection operations.

## Execution Steps

### Step 1: Create Directory Structure
- `Acme.Product.Application/Commands/Inspections`
- `Acme.Product.Application/Queries/Inspections`

### Step 2: Implement Execute Inspection
- **Command**: `ExecuteInspectionCommand` (IRequest<InspectionResultDto>)
  - Properties: `ProjectId`, `ImageId` (optional), `Parameters`
- **Handler**: `ExecuteInspectionCommandHandler`
  - Inject: `IFlowExecutionService`, `IInspectionResultRepository`, `IImageCacheRepository`.
  - Logic: 
    1. Load Project Flow.
    2. Retrieve/Acquire Image.
    3. `_flowExecutionService.ExecuteAsync()`.
    4. Save Result.
    5. Return DTO.

### Step 3: Implement Get History
- **Query**: `GetInspectionHistoryQuery` (IRequest<List<InspectionResultDto>>)
  - Properties: `ProjectId`, `StartDate`, `EndDate`, `Limit`
- **Handler**: `GetInspectionHistoryQueryHandler`
  - Logic: Repository.GetHistory, Map -> DTOs.

### Step 4: Implement Statistics
- **Query**: `GetInspectionStatisticsQuery` (IRequest<InspectionStatisticsDto>)
  - Properties: `ProjectId`
- **Handler**: `GetInspectionStatisticsQueryHandler`
  - Logic: Repository.GetStatistics.

## Outcome
- Inspection execution is decoupled.
- Statistics and History queries are optimized.
