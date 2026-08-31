import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

function readRepoText(relativePath) {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8');
}

test('continuous preview API exposes the owner-bound heartbeat endpoint', () => {
  const source = readRepoText(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/settingsApi.js');

  assert.match(
    source,
    /heartbeatContinuousPreview:\s*payload\s*=>\s*httpClient\.post\('\/cameras\/continuous-preview\/heartbeat',\s*payload\)/);
});

test('settings continuous and single-frame previews heartbeat independently of frame delivery', () => {
  const source = readRepoText(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/settings/tabs/cameraTab.js');

  assert.match(source, /startContinuousPreviewHeartbeat\(sessionId, heartbeatIntervalMs\)/);
  assert.match(source, /this\.lifecycle\.setTimeout\(async \(\) =>/);
  assert.ok(
    (source.match(/this\.startContinuousPreviewHeartbeat\(/g) || []).length >= 2,
    'both shared single-frame capture and the long-running modal must start heartbeats');
  assert.match(source, /stopHeartbeat\(\);\s*await this\.stopContinuousPreviewSession\(sessionId\)/s);
  assert.match(source, /stopPreviewHeartbeat\?\.\(\);\s*stopPreviewHeartbeat = null/s);
});

test('property-panel shared frame capture heartbeats while an external trigger is pending', () => {
  const source = readRepoText(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanelCapabilityOwner.mjs');

  assert.match(source, /function startContinuousPreviewHeartbeat\(sessionId, heartbeatIntervalMs, signal\)/);
  assert.match(source, /globalThis\.setTimeout\(async \(\) =>/);
  assert.match(source, /'\/cameras\/continuous-preview\/heartbeat'/);
  assert.match(source, /finally \{\s*stopHeartbeat\(\);\s*await httpClient\.post\('\/cameras\/continuous-preview\/stop'/s);
});
