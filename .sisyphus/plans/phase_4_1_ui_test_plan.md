# Phase 4.1: UI Automation Tests - Execution Plan

## Objective
Establish an End-to-End (E2E) testing suite using Playwright to verify the critical path of the application.

## Diagnosis
- **Current Status**: No UI tests.
- **Requirement**: Verify "Create Project -> Add Operator -> Run" flow.

## Execution Steps

### Step 1: Initialize Playwright Project
- **Command**: `npm init playwright@latest` (in `Acme.Product/tests/Acme.Product.UI.Tests`)
- **Config**: Configure for Chromium (WebView2 engine) and set base URL.

### Step 2: Create Test Project Structure
- `tests/e2e/project.spec.ts`
- `tests/e2e/editor.spec.ts`

### Step 3: Implement Critical Path Test
- **File**: `project.spec.ts`
- **Scenario**:
  1.  Launch App.
  2.  Click "New Project".
  3.  Verify Canvas is visible.
  4.  Drag "Gaussian Blur" from sidebar.
  5.  Click "Run".
  6.  Assert "Execution Completed" toast appears.

### Step 4: Integrate with CI
- Add `npx playwright test` step to `ci.yml`.

## Outcome
- Automated verification of the UI layer.
- Protection against regression in the frontend.
