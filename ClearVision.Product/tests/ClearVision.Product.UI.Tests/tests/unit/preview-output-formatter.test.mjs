import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildPreviewSummaryItems,
  formatPreviewDiagnosticMessage,
  formatPreviewOutputValue,
  getPreviewResultLabel,
  getPreviewTypeLabel,
  isPreviewTechnicalDiagnostic
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewOutputFormatter.mjs';

test('formatPreviewOutputValue summarizes detections payloads by count', () => {
  assert.deepEqual(
    formatPreviewOutputValue('Detections', {
      detections: [{ label: 'Wire_Black' }, { label: 'Wire_Red' }]
    }),
    {
      text: '2 个检测结果',
      title: null,
      kind: 'detections'
    }
  );
});

test('formatPreviewOutputValue summarizes suppressed detections by count', () => {
  assert.deepEqual(
    formatPreviewOutputValue('SuppressedDetections', [{ id: 1 }]),
    {
      text: '1 个已抑制',
      title: null,
      kind: 'suppressed'
    }
  );
});

test('formatPreviewOutputValue summarizes generic arrays and objects', () => {
  assert.deepEqual(
    formatPreviewOutputValue('Labels', ['A', 'B', 'C']),
    {
      text: '3 项',
      title: null,
      kind: 'array'
    }
  );

  assert.deepEqual(
    formatPreviewOutputValue('Meta', { station: 'S1', mode: 'Auto' }),
    {
      text: '2 个字段',
      title: null,
      kind: 'object'
    }
  );
});

test('formatPreviewOutputValue preserves numeric and boolean formatting', () => {
  assert.deepEqual(
    formatPreviewOutputValue('Score', 0.771545),
    {
      text: '0.772',
      title: null,
      kind: 'number'
    }
  );

  assert.deepEqual(
    formatPreviewOutputValue('Enabled', true),
    {
      text: '是',
      title: null,
      kind: 'boolean'
    }
  );
});

test('formatPreviewOutputValue truncates long strings without losing the full title text', () => {
  const formatted = formatPreviewOutputValue(
    'Summary',
    'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ',
    { stringMaxLength: 12 }
  );

  assert.equal(formatted.text, 'abcdef...XYZ');
  assert.equal(formatted.title, 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ');
  assert.equal(formatted.kind, 'string');
});

test('buildPreviewSummaryItems skips image-like payloads and uses Chinese field labels', () => {
  const items = buildPreviewSummaryItems({
    PreviewImage: 'data:image/png;base64,<redacted-image-payload>',
    OutputImage: 'image artifact; content omitted.',
    ResourceDescriptor: 'resource-descriptor: image artifact; content omitted.',
    diagnostics: ['Observation detail omitted because depth-limit was reached.'],
    Detections: { detections: [{}, {}] },
    Meta: { station: 'S1', mode: 'Auto' }
  }, {
    maxItems: 3,
    stringMaxLength: 16,
    skipImageLikeValues: true
  });

  assert.deepEqual(items, [
    {
      key: '检测结果',
      rawKey: 'Detections',
      value: '2 个检测结果',
      title: null,
      kind: 'detections'
    },
    {
      key: 'Meta',
      rawKey: 'Meta',
      value: '2 个字段',
      title: null,
      kind: 'object'
    }
  ]);
});

test('preview result formatter localizes labels, diagnostics, and internal type names', () => {
  assert.equal(getPreviewResultLabel('inputImage'), '输入图像');
  assert.equal(getPreviewResultLabel('$["spatialContext"]'), '空间上下文');
  assert.equal(getPreviewTypeLabel('System.Text.Json.JsonElement'), 'JSON 对象');
  assert.equal(
    formatPreviewDiagnosticMessage('Observation detail omitted because depth-limit was reached.'),
    '详情过深，已自动折叠。'
  );
  assert.equal(
    formatPreviewDiagnosticMessage('image artifact; content omitted.'),
    '图像内容已省略，可点击查看摘要/预览。'
  );
  assert.equal(isPreviewTechnicalDiagnostic('resource-descriptor depth-limit'), true);
});
