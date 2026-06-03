import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildResultDefects,
  getResultDefectCount
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/app/resultDefects.js';

test('buildResultDefects caps synthetic defects while preserving count separately', () => {
  const result = { defectCount: 10000 };
  const defects = buildResultDefects(result);

  assert.equal(defects.length, 300);
  assert.equal(getResultDefectCount(result), 10000);
  assert.deepEqual(defects[0], {
    type: 'Target 1',
    description: 'Result did not include defect details.'
  });
});

test('buildResultDefects stores compact copies of actual defects', () => {
  const largeText = 'x'.repeat(1000);
  const actualDefects = Array.from({ length: 350 }, (_, index) => ({
    Id: `actual-${index}-${largeText}`,
    Type: `type-${index}`,
    Description: largeText,
    X: index,
    Y: index + 1,
    Width: 10,
    Height: 20,
    ConfidenceScore: 0.92,
    largePayload: { image: largeText }
  }));

  const defects = buildResultDefects({ Defects: actualDefects });

  assert.equal(defects.length, 300);
  assert.equal(getResultDefectCount({ Defects: actualDefects }), 350);
  assert.notEqual(defects[0], actualDefects[0]);
  assert.equal(defects[0].largePayload, undefined);
  assert.equal(defects[0].id.length, 99);
  assert.equal(defects[0].description.length, 163);
  assert.equal(defects[0].x, 0);
  assert.equal(defects[0].confidenceScore, 0.92);
});
