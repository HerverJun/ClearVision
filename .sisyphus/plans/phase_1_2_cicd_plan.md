# Phase 1.2: Setup CI/CD Pipeline - Execution Plan

## Objective
Establish a robust Continuous Integration (CI) pipeline using GitHub Actions to automatically build and test the application on every push and pull request.

## Diagnosis
- **Current Status**: No `.github` directory found. No CI pipeline exists.
- **Requirement**: Build .NET 8 Desktop app (Windows-only due to WebView2/WinForms).

## Execution Steps

### Step 0: Initialize Git Repository
- **Command**: `git init`
- **Command**: `git branch -m main`
- **Verification**: `git status` should show clean working tree (ignoring untracked files).
- **Note**: Ensure `.gitignore` is effective (already exists).

### Step 1: Create Workflow Directory
- **Command**: `mkdir -p .github/workflows`

### Step 2: Create CI Workflow File
- **File**: `.github/workflows/ci.yml`
- **Content**:
  ```yaml
  name: ClearVision CI

  on:
    push:
      branches: [ "main", "develop" ]
    pull_request:
      branches: [ "main", "develop" ]
    workflow_dispatch:

  jobs:
    build:
      name: Build & Test
      runs-on: windows-latest

      steps:
      - name: Checkout Code
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Restore Dependencies
        run: dotnet restore Acme.Product.sln

      - name: Build (Debug)
        run: dotnet build Acme.Product.sln --configuration Debug --no-restore

      - name: Test
        run: dotnet test Acme.Product.sln --configuration Debug --no-build --verbosity normal

      - name: Publish (Release)
        run: dotnet publish Acme.Product/src/Acme.Product.Desktop/Acme.Product.Desktop.csproj -c Release -r win-x64 --self-contained true -o ./publish

      - name: Upload Artifact
        uses: actions/upload-artifact@v4
        with:
          name: ClearVision-Build
          path: ./publish
  ```

### Step 3: Verify Locally (Dry Run)
- Since we cannot run GitHub Actions locally without specific tools (like `act`), we will rely on the `dotnet` commands being valid.
- **Verification**: Run the individual `dotnet` commands locally to ensure they pass before pushing.
  - `dotnet restore Acme.Product.sln`
  - `dotnet build Acme.Product.sln`
  - `dotnet test Acme.Product.sln`
  - `dotnet publish ...`

## Outcome
- A `ci.yml` file committed to the repository.
- Automated feedback loop for future changes.
