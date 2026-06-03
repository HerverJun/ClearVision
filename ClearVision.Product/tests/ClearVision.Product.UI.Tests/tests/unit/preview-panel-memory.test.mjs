import test from 'node:test';
import assert from 'node:assert/strict';
import { PreviewPanel } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewPanel.js';

test('PreviewPanel analysis results avoid retaining input images and oversized previews', () => {
  const panel = new PreviewPanel(null, {
    maxAnalysisImageBase64Chars: 8
  });

  const normalized = panel._normalizeAnalysisResult({
    targetNodeId: 'node-1',
    success: true,
    inputImageBase64: 'INPUT_IMAGE',
    previewImageBase64: 'PREVIEW',
    outputs: { Count: 1 }
  });

  assert.equal(normalized.inputImageSrc, null);
  assert.equal(normalized.previewImageSrc, 'data:image/png;base64,PREVIEW');
  assert.deepEqual(normalized.outputs, { Count: 1 });

  const oversized = panel._normalizeAnalysisResult({
    targetNodeId: 'node-1',
    success: true,
    previewImageBase64: 'TOO_LARGE_PREVIEW'
  });

  assert.equal(oversized.previewImageSrc, null);

  panel.analysisResult = normalized;
  panel.destroy();
  assert.equal(panel.analysisResult, null);
});
