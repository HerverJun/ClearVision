# Phase 3.2: Flow Editor Core Completion - Execution Plan

## Objective
Enhance the custom Flow Editor Canvas to support professional-grade interactions: Drag & Drop from library, robust connection validation, and infinite canvas navigation.

## Diagnosis
- **Current Status**: Basic node rendering and simple connections.
- **Missing**: Drag from sidebar, Type checking on connect, Multi-select, Zoom/Pan.

## Execution Steps

### Step 1: Drag & Drop Implementation
- **Files**: `operatorLibraryPanel.js` (Source), `app.js` (Coordinator), `flowCanvas.js` (Target).
- **Logic**:
  1.  Sidebar: `dragstart` sets `dataTransfer` with Operator Type.
  2.  Canvas: `dragover` allows drop.
  3.  Canvas: `drop` gets coordinates, transforms to logical position (accounting for zoom/pan), and triggers `ExecuteOperatorCommand` (or AddOperator).

### Step 2: Connection Validation
- **File**: `flowEditorInteraction.js`
- **Logic**:
  1.  On `mouseup` over a port: Check Source and Target data types.
  2.  If compatible (e.g., Image -> Image), allow connection.
  3.  If incompatible, show error toast and snap back line.
  4.  Prevent loops (DAG check).

### Step 3: Zoom & Pan
- **File**: `flowCanvas.js`
- **Logic**:
  1.  Matrix transformation context (`ctx.setTransform`).
  2.  Events: `wheel` (Zoom), `mousedown` + `space` or Middle Click (Pan).
  3.  Update `viewport` state `{ x, y, scale }`.

### Step 4: Selection API
- **File**: `flowEditorInteraction.js`
- **Logic**:
  1.  Click node -> Select (highlight).
  2.  Shift + Click -> Multi-select.
  3.  Drag background -> Box select.
  4.  Delete Key -> Remove selected nodes/edges.

## Outcome
- A usable, intuitive Flow Editor comparable to commercial tools.
