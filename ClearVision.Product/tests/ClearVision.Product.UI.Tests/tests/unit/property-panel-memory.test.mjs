import test from 'node:test';
import assert from 'node:assert/strict';

globalThis.window = globalThis.window || {};

const { PropertyPanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanel.js');

function createDeferred() {
  let resolve;
  let reject;
  const promise = new Promise((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

function createPanelWithImageLoader(loader) {
  const panel = Object.create(PropertyPanel.prototype);
  Object.assign(panel, {
    inputImageBase64Load: null,
    loadImageUrlAsBase64: loader
  });
  return panel;
}

test('PropertyPanel deduplicates concurrent cached image base64 loads', async () => {
  const calls = [];
  const deferred = createDeferred();
  const panel = createPanelWithImageLoader((url) => {
    calls.push(url);
    return deferred.promise;
  });

  const first = panel.loadInputImageUrlAsBase64('/images/latest');
  const second = panel.loadInputImageUrlAsBase64('/images/latest');

  assert.equal(first, second);
  await Promise.resolve();
  assert.deepEqual(calls, ['/images/latest']);

  deferred.resolve('AQID');

  assert.equal(await first, 'AQID');
  assert.equal(await second, 'AQID');
  assert.equal(panel.inputImageBase64Load, null);
});

test('PropertyPanel keeps newer cached image load when an older load resolves', async () => {
  const deferredA = createDeferred();
  const deferredB = createDeferred();
  const calls = [];
  const panel = createPanelWithImageLoader((url) => {
    calls.push(url);
    return url.endsWith('/a') ? deferredA.promise : deferredB.promise;
  });

  const first = panel.loadInputImageUrlAsBase64('/images/a');
  const second = panel.loadInputImageUrlAsBase64('/images/b');

  assert.notEqual(first, second);
  await Promise.resolve();
  assert.deepEqual(calls, ['/images/a', '/images/b']);
  assert.equal(panel.inputImageBase64Load.sourceKey, '/images/b');

  deferredA.resolve('A');
  assert.equal(await first, 'A');
  assert.equal(panel.inputImageBase64Load.sourceKey, '/images/b');

  deferredB.resolve('B');
  assert.equal(await second, 'B');
  assert.equal(panel.inputImageBase64Load, null);
});

test('PropertyPanel releases cached image in-flight state after loader failure', async () => {
  const panel = createPanelWithImageLoader(() => {
    throw new Error('load failed');
  });

  await assert.rejects(
    panel.loadInputImageUrlAsBase64('/images/failure'),
    /load failed/
  );

  assert.equal(panel.inputImageBase64Load, null);
});
