import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildResultCardsFromOutputData,
  inferPortDataType,
  renderPortDataTypeValue,
  renderResultCardHtml,
  summarizeResultField
} from '../../../../src/Acme.Product.Desktop/wwwroot/src/features/results/portDataTypeRenderer.mjs';

test('infers scalar and geometry port data types', () => {
  assert.equal(inferPortDataType('Score', 0.984), 'Float');
  assert.equal(inferPortDataType('Count', 3), 'Integer');
  assert.equal(inferPortDataType('IsOk', true), 'Boolean');
  assert.equal(inferPortDataType('Center', { x: 10, y: 20 }), 'Point');
  assert.equal(inferPortDataType('Box', { x: 1, y: 2, width: 30, height: 40 }), 'Rectangle');
  assert.equal(inferPortDataType('Circle', { centerX: 4, centerY: 5, radius: 6 }), 'CircleData');
  assert.equal(inferPortDataType('Line', { x1: 1, y1: 2, x2: 3, y2: 4 }), 'LineData');
  assert.equal(inferPortDataType('Line', { startX: 1, startY: 2, endX: 3, endY: 4 }), 'LineData');
});

test('renders detection lists with labels, scores, and boxes', () => {
  const html = renderPortDataTypeValue({
    key: 'Detections',
    value: [
      { label: 'Wire_Red', confidence: 0.931, box: { x: 1, y: 2, width: 3, height: 4 } },
      { className: 'Wire_Black', score: 88.2 }
    ]
  });

  assert.match(html, /Wire_Red/);
  assert.match(html, /93\.1%/);
  assert.match(html, /Wire_Black/);
  assert.match(html, /88\.2%/);
  assert.match(html, /x 1, y 2, w 3, h 4/);
});

test('renders detection boxes from top-level detection coordinates', () => {
  const html = renderPortDataTypeValue({
    key: 'SuppressedDetections',
    value: [
      { Label: 'Scratch', Confidence: 0.75, X: 11, Y: 12, Width: 13, Height: 14 }
    ]
  });

  assert.match(html, /Scratch/);
  assert.match(html, /75\.0%/);
  assert.match(html, /x 11, y 12, w 13, h 14/);
});

test('builds typed result cards from mixed output data', () => {
  const cards = buildResultCardsFromOutputData({
    Text: 'ABC-123',
    Width: 12.3456,
    IsOk: false,
    Detections: [{ label: 'Scratch' }],
    Response: { statusCode: 200, body: 'OK' },
    OutputImage: 'data:image/png;base64,abc'
  }, {
    status: 'NG'
  });

  assert.equal(cards.some(card => card.category === 'recognition'), true);
  assert.equal(cards.some(card => card.category === 'measurement'), true);
  assert.equal(cards.some(card => card.category === 'boolean'), true);
  assert.equal(cards.some(card => card.category === 'detection'), true);
  assert.equal(cards.some(card => card.category === 'communication'), true);
  assert.equal(cards.flatMap(card => card.fields).some(field => field.key === 'OutputImage'), false);
});

test('filters raw base64 image payloads with camelCase keys', () => {
  const cards = buildResultCardsFromOutputData({
    outputImageBase64: 'A'.repeat(180),
    PreviewImage: 'B'.repeat(180),
    Width: 42
  });

  const keys = cards.flatMap(card => card.fields).map(field => field.key);
  assert.deepEqual(keys, ['Width']);
});

test('renders full result card html and compact summaries', () => {
  const card = {
    id: 'measurement-card',
    category: 'measurement',
    title: 'Measurements',
    status: 'OK',
    fields: [
      { key: 'Width', label: 'Width', value: 9.8765, unit: 'mm', dataType: 'Float' }
    ]
  };

  const html = renderResultCardHtml(card);
  assert.match(html, /Measurements/);
  assert.match(html, /9\.877/);
  assert.match(html, /mm/);

  assert.equal(summarizeResultField(card.fields[0]), '9.877 mm');
});
