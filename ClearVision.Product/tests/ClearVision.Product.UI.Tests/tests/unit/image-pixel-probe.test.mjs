import test from 'node:test';
import assert from 'node:assert/strict';

import {
  ImagePixelProbe,
  PIXEL_PROBE_LOADING_MESSAGE,
  PIXEL_PROBE_NO_IMAGE_MESSAGE,
  PIXEL_PROBE_OUTSIDE_MESSAGE,
  mapPointToImagePixel,
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/imagePixelProbe.mjs';

function fakeRect(left, top, width, height) {
  return {
    left,
    top,
    width,
    height,
    right: left + width,
    bottom: top + height,
  };
}

function fakeImage({
  naturalWidth = 100,
  naturalHeight = 50,
  rect = fakeRect(10, 20, 200, 100),
  src = 'data:image/png;base64,IMAGE_A',
  complete = true,
} = {}) {
  return {
    naturalWidth,
    naturalHeight,
    currentSrc: src,
    src,
    complete,
    getAttribute(name) {
      return name === 'src' ? src : null;
    },
    getBoundingClientRect() {
      return rect;
    },
  };
}

function createCanvasFactory(drawCalls, pixelFor = () => [10, 20, 30, 255]) {
  return (width, height) => ({
    width,
    height,
    getContext() {
      return {
        drawImage(...args) {
          drawCalls.push(args);
        },
        getImageData(x, y) {
          return {
            data: Uint8ClampedArray.from(pixelFor(x, y)),
          };
        },
      };
    },
  });
}

test('image pixel probe maps the center point to original image coordinates', () => {
  const mapped = mapPointToImagePixel({
    clientX: 110,
    clientY: 70,
    naturalWidth: 100,
    naturalHeight: 50,
    elementRect: fakeRect(10, 20, 200, 100),
  });

  assert.equal(mapped.inside, true);
  assert.equal(mapped.x, 50);
  assert.equal(mapped.y, 25);
  assert.equal(mapped.width, 100);
  assert.equal(mapped.height, 50);
  assert.equal(mapped.scale, 2);
});

test('image pixel probe treats fit-mode letterbox space as outside the image', () => {
  const mapped = mapPointToImagePixel({
    clientX: 30,
    clientY: 50,
    naturalWidth: 100,
    naturalHeight: 100,
    elementRect: fakeRect(0, 0, 200, 100),
  });

  assert.equal(mapped.inside, false);
  assert.equal(mapped.reason, 'outside');
});

test('image pixel probe clamps coordinates at the image boundary', () => {
  const mapped = mapPointToImagePixel({
    clientX: 210,
    clientY: 120,
    naturalWidth: 100,
    naturalHeight: 50,
    elementRect: fakeRect(10, 20, 200, 100),
  });

  assert.equal(mapped.inside, true);
  assert.equal(mapped.x, 99);
  assert.equal(mapped.y, 49);
});

test('image pixel probe reads RGB and grayscale values from a cached canvas', () => {
  const drawCalls = [];
  const probe = new ImagePixelProbe({
    createCanvas: createCanvasFactory(drawCalls, (x, y) => [x, y, 30, 255]),
  });
  const image = fakeImage();

  const result = probe.probePoint({ clientX: 110, clientY: 70 }, image);
  assert.equal(result.kind, 'pixel');
  assert.match(result.message, /X: 50  Y: 25/);
  assert.match(result.message, /RGB: 50,25,30/);
  assert.match(result.message, /灰度≈/);
  assert.match(result.message, /图像: 100x50/);
  assert.match(result.message, /缩放: 200%/);
  assert.equal(drawCalls.length, 1);

  const second = probe.probePoint({ clientX: 112, clientY: 72 }, image);
  assert.equal(second.kind, 'pixel');
  assert.equal(drawCalls.length, 1);
});

test('image pixel probe displays grayscale when RGB channels match', () => {
  const probe = new ImagePixelProbe({
    createCanvas: createCanvasFactory([], () => [42, 42, 42, 255]),
  });

  const result = probe.probePoint({ clientX: 110, clientY: 70 }, fakeImage());

  assert.equal(result.kind, 'pixel');
  assert.match(result.message, /灰度: 42/);
  assert.doesNotMatch(result.message, /RGB:/);
});

test('image pixel probe invalidates the canvas cache when the image changes or cache is reset', () => {
  const drawCalls = [];
  const probe = new ImagePixelProbe({
    createCanvas: createCanvasFactory(drawCalls),
  });

  probe.probePoint({ clientX: 110, clientY: 70 }, fakeImage({ src: 'data:image/png;base64,A' }));
  probe.probePoint({ clientX: 110, clientY: 70 }, fakeImage({ src: 'data:image/png;base64,A' }));
  assert.equal(drawCalls.length, 1);

  probe.probePoint({ clientX: 110, clientY: 70 }, fakeImage({ src: 'data:image/png;base64,B' }));
  assert.equal(drawCalls.length, 2);

  probe.reset();
  probe.probePoint({ clientX: 110, clientY: 70 }, fakeImage({ src: 'data:image/png;base64,B' }));
  assert.equal(drawCalls.length, 3);
});

test('image pixel probe returns understandable statuses without a loaded image', () => {
  const probe = new ImagePixelProbe({
    createCanvas: createCanvasFactory([]),
  });

  assert.equal(probe.probePoint({ clientX: 1, clientY: 1 }, null).message, PIXEL_PROBE_NO_IMAGE_MESSAGE);
  assert.equal(
    probe.probePoint({ clientX: 1, clientY: 1 }, fakeImage({ complete: false })).message,
    PIXEL_PROBE_LOADING_MESSAGE
  );
  assert.equal(
    probe.probePoint({ clientX: 1, clientY: 1 }, fakeImage({ rect: fakeRect(10, 10, 100, 100) })).message,
    PIXEL_PROBE_OUTSIDE_MESSAGE
  );
});
