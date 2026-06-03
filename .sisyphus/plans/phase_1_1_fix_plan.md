# Phase 1.1: Fix Build & Dependencies - Implementation Plan

## Diagnosis
The project fails to build due to missing NuGet packages in specific projects and missing `using` directives in source files.

### 1. Project Dependencies (NuGet)
- **ClearVision.Product.Desktop**:
  - Missing `Microsoft.EntityFrameworkCore` (Required for `AddDbContext`).
  - Missing `Microsoft.Extensions.Logging` (Required for `ILoggerFactory`).
- **ClearVision.Product.Infrastructure**:
  - Missing `Microsoft.Extensions.Logging.Abstractions` (Required for `ILogger<>`).

### 2. Source Code Issues
- **DependencyInjection.cs**: Missing imports for EF Core and Logging.
- **MatPool.cs**: Missing `using OpenCvSharp;`.
- **ThresholdOperator.cs**: Missing `using OpenCvSharp;`.
- **FlowExecutionService.cs**: Missing `using Microsoft.Extensions.Logging;`.
- **SerilogConfiguration.cs**: Missing `using Serilog;`, `using Serilog.Events;`.

## Execution Steps

### Step 1: Add NuGet Packages
```bash
# Desktop Project
dotnet add ClearVision.Product/src/ClearVision.Product.Desktop/ClearVision.Product.Desktop.csproj package Microsoft.EntityFrameworkCore --version 8.0.0
dotnet add ClearVision.Product/src/ClearVision.Product.Desktop/ClearVision.Product.Desktop.csproj package Microsoft.Extensions.Logging --version 8.0.0

# Infrastructure Project
dotnet add ClearVision.Product/src/ClearVision.Product.Infrastructure/ClearVision.Product.Infrastructure.csproj package Microsoft.Extensions.Logging.Abstractions --version 8.0.0
```

### Step 2: Fix `DependencyInjection.cs`
**File**: `ClearVision.Product/src/ClearVision.Product.Desktop/DependencyInjection.cs`
**Add**:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ClearVision.Product.Infrastructure.Data;
```

### Step 3: Fix `MatPool.cs`
**File**: `ClearVision.Product/src/ClearVision.Product.Infrastructure/ImageProcessing/MatPool.cs`
**Add**:
```csharp
using OpenCvSharp;
```

### Step 4: Fix `ThresholdOperator.cs`
**File**: `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/ThresholdOperator.cs`
**Add**:
```csharp
using OpenCvSharp;
```

### Step 5: Fix `FlowExecutionService.cs`
**File**: `ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/FlowExecutionService.cs`
**Add**:
```csharp
using Microsoft.Extensions.Logging;
```

### Step 6: Fix `SerilogConfiguration.cs`
**File**: `ClearVision.Product/src/ClearVision.Product.Infrastructure/Logging/SerilogConfiguration.cs`
**Add**:
```csharp
using Serilog;
using Serilog.Events;
```

### Step 7: Verify Build
```bash
dotnet build --no-incremental
```
