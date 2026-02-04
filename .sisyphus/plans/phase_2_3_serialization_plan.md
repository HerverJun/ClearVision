# Phase 2.3: Serialization & Export - Execution Plan

## Objective
Implement functionality to save/load projects from disk (JSON format) and export inspection results to CSV.

## Diagnosis
- **Current Status**: Projects are stored in DB via EF Core. No disk export.
- **Decision**: JSON for Projects, CSV for Results.

## Execution Steps

### Step 1: Add Dependencies
- **Project**: `Acme.Product.Infrastructure`
- **Package**: `CsvHelper` (latest version)

### Step 2: Implement Project Serialization
- **Interface**: `IProjectSerializer`
- **Implementation**: `ProjectJsonSerializer`
  - Method: `Task<byte[]> SerializeAsync(ProjectDto project)`
  - Method: `Task<ProjectDto> DeserializeAsync(byte[] data)`
  - Logic: Use `System.Text.Json` with `JsonSerializerOptions` (Indented, CamelCase).

### Step 3: Implement Result Export
- **Interface**: `IResultExporter`
- **Implementation**: `CsvResultExporter`
  - Method: `Task<byte[]> ExportToCsvAsync(List<InspectionResultDto> results)`
  - Logic: Use `CsvHelper` to write records to a `MemoryStream`.
  - Mapping: Ensure `Defects` list is formatted readable (e.g., semicolon separated string).

### Step 4: Register Services
- Update `DependencyInjection.cs` in `Infrastructure` or `Desktop` to register these services.

## Outcome
- Users can "Save Project As..." to disk.
- Users can "Export Results" to view in Excel.
