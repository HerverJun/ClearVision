# Phase 3.1: Real-time Image Streaming - Execution Plan

## Objective
Implement high-performance, zero-copy image streaming from the C# backend to the WebView2 frontend using Shared Memory.

## Diagnosis
- **Current Status**: Images passed via Base64 (slow).
- **Target**: `CoreWebView2SharedBuffer`.

## Execution Steps

### Step 1: Update WebView2Host (Backend)
- **File**: `Acme.Product.Desktop/WebView2Host.cs`
- **Logic**:
  1.  Create `CoreWebView2SharedBuffer` using `Environment.CreateSharedBuffer`.
  2.  Copy `Mat.Data` (OpenCvSharp) to the shared buffer stream.
  3.  Call `PostSharedBufferToScript(buffer, additionalData, access)`.
  4.  Send metadata (width, height, timestamp) as additional JSON data.

### Step 2: Update WebMessageBridge (Frontend)
- **File**: `wwwroot/src/core/messaging/webMessageBridge.js`
- **Logic**:
  1.  Listen for `chrome.webview.sharedbufferreceived`.
  2.  Access buffer via `event.getBuffer()`.
  3.  Wrap in `Uint8ClampedArray`.
  4.  Dispatch to `ImageStore` or `Canvas`.

### Step 3: Efficient Rendering
- **File**: `wwwroot/src/core/canvas/imageCanvas.js`
- **Logic**:
  1.  Receive `Uint8Array` (RGBA data).
  2.  Use `ImageData(data, width, height)`.
  3.  Put to Canvas Context (`putImageData` or `createImageBitmap` for Offscreen).

### Step 4: MatPool Integration
- Ensure `Mat` objects are rented/returned correctly to avoid memory spikes during streaming.

## Outcome
- 30FPS+ 1080p video stream in the UI.
- Low CPU usage.
