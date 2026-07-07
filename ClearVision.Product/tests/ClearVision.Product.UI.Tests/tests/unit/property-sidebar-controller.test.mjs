import test from 'node:test';
import assert from 'node:assert/strict';
import {
  PROPERTY_SIDEBAR_DEFAULT_WIDTH,
  PROPERTY_SIDEBAR_MAX_WIDTH,
  PROPERTY_SIDEBAR_MIN_WIDTH,
  clampWidth,
  getMaxWidth,
  readSavedWidth
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertySidebarController.mjs';

function createStorage(value) {
  return {
    getItem() {
      return value;
    }
  };
}

test('readSavedWidth falls back to default width when storage is unavailable', () => {
  assert.equal(
    readSavedWidth({ storage: null, viewportWidth: 1400 }),
    PROPERTY_SIDEBAR_DEFAULT_WIDTH
  );
});

test('clampWidth enforces the configured minimum and maximum bounds', () => {
  assert.equal(clampWidth(120, 1400), PROPERTY_SIDEBAR_MIN_WIDTH);
  assert.equal(clampWidth(2000, 1920), PROPERTY_SIDEBAR_MAX_WIDTH);
  assert.equal(clampWidth(900, 1400), 640);
  assert.equal(clampWidth(900, 1000), PROPERTY_SIDEBAR_MIN_WIDTH);
});

test('readSavedWidth ignores invalid saved values', () => {
  assert.equal(
    readSavedWidth({ storage: createStorage('not-a-number'), viewportWidth: 1400 }),
    PROPERTY_SIDEBAR_DEFAULT_WIDTH
  );
});

test('readSavedWidth re-clamps a valid saved width when the viewport shrinks', () => {
  assert.equal(
    readSavedWidth({ storage: createStorage('520'), viewportWidth: 1000 }),
    PROPERTY_SIDEBAR_MIN_WIDTH
  );
});

test('getMaxWidth allows a wider preview workbench while reserving flow editor space', () => {
  assert.equal(getMaxWidth(1024), PROPERTY_SIDEBAR_MIN_WIDTH);
  assert.equal(getMaxWidth(1366), 606);
  assert.equal(getMaxWidth(1440), 680);
  assert.equal(getMaxWidth(1920), PROPERTY_SIDEBAR_MAX_WIDTH);
});
