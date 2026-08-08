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
import {
  classifyPreviewValue,
  formatPreviewSemanticValue
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewValueSemantics.mjs';

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
  assert.equal(getPreviewResultLabel('BlobCount'), 'Blob数量（过滤后）');
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

test('preview semantic classifier distinguishes empty, absent, wrapped, and truncated collections', () => {
  assert.equal(formatPreviewSemanticValue({ value: [] }).text, '0 项');
  assert.equal(formatPreviewSemanticValue({ value: [1, 2, 3, 4, 5] }).text, '5 项');
  assert.equal(formatPreviewSemanticValue({ value: null, declaredPortDataType: 'PointList' }).text, '无输出');
  assert.equal(formatPreviewSemanticValue({ value: { Count: 5, Items: [{}, {}] }, declaredPortDataType: 'PointList' }).text, '2 / 5 项，已截断');
  assert.equal(formatPreviewSemanticValue({ value: { Count: 5, Detections: [{}, {}, {}, {}, {}] }, declaredPortDataType: 'DetectionList' }).text, '5 个检测结果');
  assert.equal(formatPreviewSemanticValue({
    value: Array.from({ length: 64 }),
    declaredPortDataType: 'PointList',
    observationNode: { semanticKind: 'collection', visibleItemCount: 64, totalItemCount: 120, truncated: true }
  }).text, '64 / 120 项，已截断');
});

test('preview semantic classifier does not confuse child or descriptor fields with business counts', () => {
  const detectionObservation = {
    kind: 'detectionList',
    semanticKind: 'detection-list',
    visibleItemCount: 5,
    totalItemCount: 5,
    children: [{ name: 'Count' }, { name: 'Detections' }]
  };
  assert.equal(formatPreviewSemanticValue({
    key: 'Result',
    value: { Count: 5, Detections: [{}, {}, {}, {}, {}] },
    declaredPortDataType: 'DetectionList',
    observationNode: detectionObservation
  }).text, '5 个检测结果');

  assert.equal(formatPreviewSemanticValue({ value: { a: 1, b: 2, c: 3 } }).text, '3 个字段');
  assert.equal(formatPreviewSemanticValue({
    value: { kind: 'unsupportedEnumerable', displayValue: 'Unknown enumerable', originalType: 'LazyRows' }
  }).text, '暂不支持展示此结果类型');
});

test('preview semantic classifier prioritizes declared list types and formats geometry', () => {
  for (const dataType of ['BlobList', 'BlobFeatureList', 'PointList']) {
    const classification = classifyPreviewValue({ value: Array.from({ length: 5 }), declaredPortDataType: dataType });
    assert.equal(classification.totalItemCount, 5);
  }
  assert.equal(formatPreviewSemanticValue({ value: Array.from({ length: 5 }), declaredPortDataType: 'BlobList' }).text, '5 项');
  assert.equal(formatPreviewSemanticValue({ value: [], declaredPortDataType: 'BlobFeatureList' }).text, '0 项');
  assert.equal(formatPreviewSemanticValue({ value: { X: 10, Y: 20 }, declaredPortDataType: 'Point' }).text, '(10, 20)');
  assert.equal(formatPreviewSemanticValue({ value: { X: 1, Y: 2, Width: 30, Height: 40 }, declaredPortDataType: 'Rectangle' }).text, '1, 2, 30 × 40');
  assert.equal(formatPreviewSemanticValue({ value: { CenterX: 4, CenterY: 5, Radius: 6 }, declaredPortDataType: 'CircleData' }).text, '中心 (4, 5)，半径 6');
  assert.match(formatPreviewSemanticValue({ value: { X1: 0, Y1: 0, X2: 3, Y2: 4 }, declaredPortDataType: 'LineData' }).text, /长度 5$/);
});
