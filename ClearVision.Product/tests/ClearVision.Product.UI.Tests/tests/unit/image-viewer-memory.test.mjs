import test from 'node:test';
import assert from 'node:assert/strict';
import { ImageViewerComponent } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/image-viewer/imageViewer.js';

function createHeadlessViewer() {
  const calls = [];
  const viewer = Object.create(ImageViewerComponent.prototype);
  viewer.currentImage = null;
  viewer.currentImageSource = null;
  viewer.currentImageSourceKey = null;
  viewer.imageCanvas = {
    loadImageData(bytes, format) {
      const image = { width: 1, height: 1 };
      calls.push({ bytes: Array.from(bytes), format });
      viewer.currentImage = image;
      return Promise.resolve(image);
    },
    resize() {
      calls.push({ resize: true });
    },
    render() {
      calls.push({ render: true });
    }
  };
  viewer.loadFromUrl = async (url) => {
    const image = { width: 1, height: 1 };
    calls.push({ url });
    viewer.currentImageSource = url;
    viewer.currentImageSourceKey = `url:${url}`;
    viewer.currentImage = image;
    return image;
  };

  return { viewer, calls };
}

function createEventTargetRecorder() {
  const listeners = [];
  return {
    listeners,
    addEventListener(type, handler, options) {
      listeners.push({ type, handler, options, removed: false });
    },
    removeEventListener(type, handler, options) {
      const listener = listeners.find(item =>
        item.type === type
        && item.handler === handler
        && item.options === options
        && item.removed === false);
      if (listener) {
        listener.removed = true;
      }
    }
  };
}

function createDefectViewer() {
  const overlays = [];
  const defectList = {
    innerHTML: '',
    querySelectorAll() {
      return [];
    }
  };
  const viewer = Object.create(ImageViewerComponent.prototype);
  Object.assign(viewer, {
    container: {
      querySelector(selector) {
        return selector === '#defect-list' ? defectList : null;
      },
      querySelectorAll() {
        return [];
      }
    },
    imageCanvas: {
      overlays,
      clearOverlays() {
        overlays.length = 0;
      },
      addOverlay(type, x, y, width, height, options) {
        const overlay = { type, x, y, width, height, ...options };
        overlays.push(overlay);
        return overlay;
      },
      render() {}
    },
    defects: [],
    omittedDefectCount: 0
  });

  return { viewer, overlays, defectList };
}

test('ImageViewer loads data image URLs through byte arrays instead of retaining base64 URLs', async () => {
  const { viewer, calls } = createHeadlessViewer();
  const dataUrl = 'data:image/jpeg;base64,AQIDBA==';

  await viewer.loadImage(dataUrl, { silent: true });

  assert.deepEqual(calls, [{ bytes: [1, 2, 3, 4], format: 'jpeg' }]);
  assert.equal(viewer.currentImageSource, null);
});

test('ImageViewer loads raw base64 through byte arrays', async () => {
  const { viewer, calls } = createHeadlessViewer();

  await viewer.loadImage('BQY=', { silent: true });

  assert.deepEqual(calls, [{ bytes: [5, 6], format: 'png' }]);
  assert.equal(viewer.currentImageSource, null);
});

test('ImageViewer keeps non-base64 data URLs on the URL path', async () => {
  const { viewer, calls } = createHeadlessViewer();
  const svgUrl = 'data:image/svg+xml,%3Csvg%3E%3C/svg%3E';

  await viewer.loadImage(svgUrl, { silent: true });

  assert.deepEqual(calls, [{ url: svgUrl }]);
  assert.equal(viewer.currentImageSource, svgUrl);
});

test('ImageViewer loads cached image URLs through the URL path', async () => {
  const { viewer, calls } = createHeadlessViewer();
  const url = 'http://localhost:5000/api/images/image-1';

  await viewer.loadImage(url, { silent: true });

  assert.deepEqual(calls, [{ url }]);
  assert.equal(viewer.currentImageSource, url);
});

test('ImageViewer loads root-relative cached image URLs through the URL path', async () => {
  const { viewer, calls } = createHeadlessViewer();
  const url = '/api/images/image-2';

  await viewer.loadImage(url, { silent: true });

  assert.deepEqual(calls, [{ url }]);
  assert.equal(viewer.currentImageSource, url);
});

test('ImageViewer does not confuse slash-prefixed raw base64 with root-relative URLs', async () => {
  const { viewer, calls } = createHeadlessViewer();

  await viewer.loadImage('/w==', { silent: true });

  assert.deepEqual(calls, [{ bytes: [255], format: 'png' }]);
  assert.equal(viewer.currentImageSource, null);
});

test('ImageViewer skips decoding repeated base64 sources using a lightweight source key', async () => {
  const { viewer, calls } = createHeadlessViewer();
  const dataUrl = 'data:image/png;base64,AQID';

  const firstImage = await viewer.loadImage(dataUrl, { silent: true });
  const secondImage = await viewer.loadImage(dataUrl, { silent: true });

  assert.equal(secondImage, firstImage);
  assert.deepEqual(calls, [
    { bytes: [1, 2, 3], format: 'png' },
    { resize: true },
    { render: true }
  ]);
  assert.notEqual(viewer.currentImageSourceKey, dataUrl);
  assert.match(viewer.currentImageSourceKey, /^base64:png:/);
});

test('ImageViewer caps displayed defects and stores compact annotation payloads', () => {
  const { viewer, overlays, defectList } = createDefectViewer();
  const largeText = 'x'.repeat(1000);
  const sourceDefects = Array.from({ length: 350 }, (_, index) => ({
    id: `defect-${index}-${largeText}`,
    type: `type-${index}`,
    description: largeText,
    x: index,
    y: index + 1,
    width: 10,
    height: 20,
    confidenceScore: 0.91,
    largePayload: { image: largeText }
  }));

  viewer.showDefects(sourceDefects);

  assert.equal(viewer.defects.length, 300);
  assert.equal(overlays.length, 300);
  assert.equal(viewer.omittedDefectCount, 50);
  assert.notEqual(viewer.defects[0], sourceDefects[0]);
  assert.notEqual(overlays[0].data, sourceDefects[0]);
  assert.equal(viewer.defects[0].largePayload, undefined);
  assert.equal(viewer.defects[0].description.length, 123);
  assert.equal(viewer.defects[0].id.length, 99);
  assert.match(defectList.innerHTML, /Hidden 50 more defects/);
  assert.doesNotMatch(defectList.innerHTML, /x{200}/);
});

test('ImageViewer destroy releases listeners, canvas resources, callbacks, and current image', () => {
  const toolbarTargets = {
    '#btn-zoom-in': createEventTargetRecorder(),
    '#btn-zoom-out': createEventTargetRecorder(),
    '#btn-fit-window': createEventTargetRecorder(),
    '#btn-actual-size': createEventTargetRecorder()
  };
  const canvasTarget = createEventTargetRecorder();
  let destroyed = 0;
  const imageCanvas = {
    canvas: canvasTarget,
    overlays: [],
    loadImage() {
      return Promise.resolve({ width: 1, height: 1 });
    },
    render() {},
    destroy() {
      destroyed += 1;
    }
  };
  const container = {
    innerHTML: '<canvas></canvas>',
    querySelector(selector) {
      return toolbarTargets[selector] ?? null;
    },
    contains(target) {
      return target === canvasTarget;
    }
  };
  const viewer = Object.create(ImageViewerComponent.prototype);
  Object.assign(viewer, {
    container,
    canvas: canvasTarget,
    imageCanvas,
    currentImage: { bytes: new Uint8Array([1, 2, 3]) },
    currentImageSource: 'data:image/png;base64,AQID',
    currentImageSourceKey: 'base64:png:4:key',
    defects: [{ id: 1 }],
    onRegionSelected() {},
    onAnnotationClicked() {},
    onImageLoaded() {},
    _eventDisposers: [],
    _originalImageCanvasLoadImage: null,
    _originalImageCanvasRender: null,
    _isDestroyed: false
  });

  viewer.bindToolbarEvents();
  viewer.bindCanvasEvents();

  assert.equal(viewer.isAttachedTo(container), true);
  assert.equal(Object.values(toolbarTargets).flatMap(target => target.listeners).length, 4);
  assert.equal(canvasTarget.listeners.length, 1);

  viewer.destroy();
  viewer.destroy();

  assert.equal(destroyed, 1);
  assert.equal(viewer.currentImage, null);
  assert.equal(viewer.currentImageSource, null);
  assert.equal(viewer.currentImageSourceKey, null);
  assert.deepEqual(viewer.defects, []);
  assert.equal(viewer.onRegionSelected, null);
  assert.equal(viewer.onAnnotationClicked, null);
  assert.equal(viewer.onImageLoaded, null);
  assert.equal(viewer.imageCanvas, null);
  assert.equal(viewer.canvas, null);
  assert.equal(container.innerHTML, '');
  assert.equal(Object.values(toolbarTargets).flatMap(target => target.listeners).every(listener => listener.removed), true);
  assert.equal(canvasTarget.listeners.every(listener => listener.removed), true);
});
