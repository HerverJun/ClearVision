import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {
  buildVisibleObservationRows,
  nodePreviewRendererRegistry,
  searchObservationRows
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/nodePreviewInspector.js';
import {
  createNodePreviewSelectionStore,
  getNodePreviewIdentitySignature
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/nodePreviewSelectionStore.js';

function identity(overrides = {}) {
  return {
    projectId: 'project-1',
    targetNodeId: 'node-1',
    debugSessionId: 'debug-1',
    clientRequestSequence: 7,
    flowRevision: 11,
    ...overrides
  };
}

function node(kind, fields = {}) {
  return {
    kind,
    displayValue: fields.displayValue ?? `${kind} value`,
    originalType: fields.originalType ?? null,
    name: fields.name ?? kind,
    pathHint: fields.pathHint ?? `$["${fields.name ?? kind}"]`,
    addressable: fields.addressable ?? true,
    truncated: fields.truncated ?? false,
    children: fields.children ?? [],
    artifact: fields.artifact ?? null
  };
}

test('NodePreviewInspector renderer registry covers bounded Observation DTO kinds', () => {
  assert.deepEqual(
    nodePreviewRendererRegistry.coverage(),
    [
      'scalar',
      'point',
      'circle',
      'line',
      'rectangle',
      'resource',
      'detectionList',
      'detection',
      'calibrationQuality',
      'container',
      'bounded',
      'unknown'
    ]
  );

  assert.equal(nodePreviewRendererRegistry.render(node('number')).renderer, 'scalar');
  assert.equal(nodePreviewRendererRegistry.render(node('object')).renderer, 'container');
  assert.equal(nodePreviewRendererRegistry.render(node('detectionList')).renderer, 'detectionList');
  assert.equal(nodePreviewRendererRegistry.render(node('image')).renderer, 'resource');
  assert.equal(nodePreviewRendererRegistry.render(node('unknownKind')).renderer, 'unknown');
  assert.equal(
    nodePreviewRendererRegistry.render(node('object', { originalType: 'OpenCvSharp.Point2f' })).label,
    'Point'
  );
  assert.equal(
    nodePreviewRendererRegistry.render(node('object', { originalType: 'CalibrationQuality' })).label,
    'Calibration Quality'
  );
});

test('NodePreviewInspector tree rendering is row-limited and searches only bounded DTO fields', () => {
  const children = Array.from({ length: 240 }, (_, index) => node('number', {
    name: `Field${index}`,
    displayValue: `Value${index}`,
    pathHint: `$["Field${index}"]`
  }));
  const root = node('dictionary', {
    name: null,
    pathHint: '$',
    addressable: false,
    children
  });
  root.outputData = 'SECRET_OUTSIDE_DTO';

  const rows = buildVisibleObservationRows(root, {
    expandedKeys: new Set(),
    limit: 50
  });

  assert.equal(rows.rows.length, 50);
  assert.equal(rows.hasMore, true);
  assert.ok(rows.rows.every(row => row.normalized.pathHint.startsWith('$')));

  const matched = searchObservationRows(root, 'Field120', 10);
  assert.equal(matched.rows.length, 1);
  assert.equal(matched.rows[0].normalized.name, 'Field120');

  const outside = searchObservationRows(root, 'SECRET_OUTSIDE_DTO', 10);
  assert.equal(outside.rows.length, 0);
});

test('NodePreviewInspector preserves malicious display strings as text data and does not use HTML injection APIs', () => {
  const malicious = '<img src=x onerror=alert(1)><script>alert(2)</script>';
  const root = node('string', {
    name: 'Unsafe',
    displayValue: malicious,
    pathHint: '$["Unsafe"]'
  });
  const rows = buildVisibleObservationRows(root, { limit: 5 });

  assert.equal(rows.rows[0].normalized.displayValue, malicious);
  assert.equal(rows.rows[0].rendered.value, malicious);

  const sourcePath = path.resolve(
    process.cwd(),
    '../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/nodePreviewInspector.js'
  );
  const source = fs.readFileSync(sourcePath, 'utf8');
  assert.equal(source.includes('.innerHTML'), false);
  assert.equal(/\bfetch\s*\(/.test(source), false);
});

test('nodePreviewSelectionStore stores complete identity and clears on identity or status boundaries', () => {
  const store = createNodePreviewSelectionStore();
  const selected = store.select({
    identity: identity(),
    nodeName: 'Threshold',
    nodeKind: 'Thresholding',
    displayValue: '0.98',
    originalType: 'System.Double',
    pathHint: '$["Score"]',
    addressable: false,
    artifact: {
      artifactId: 'artifact-1',
      kind: 'image',
      role: 'outputImage',
      pathHint: '$["Image"]',
      contentType: 'image/png',
      length: 4,
      sha256: 'abc'
    }
  });

  assert.equal(selected.identitySignature, getNodePreviewIdentitySignature(identity()));
  assert.equal(selected.addressable, false);
  assert.equal(selected.pathHint, '$["Score"]');
  assert.equal(store.getSelection().artifact.artifactId, 'artifact-1');

  store.clearIfIdentityChanged(identity({ flowRevision: 12 }));
  assert.equal(store.getSelection(), null);

  store.select({ identity: identity(), displayValue: 'x', pathHint: '$["x"]' });
  store.clear();
  assert.equal(store.getSelection(), null);
});
