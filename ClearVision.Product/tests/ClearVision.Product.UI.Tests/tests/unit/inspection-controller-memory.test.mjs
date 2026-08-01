import test from 'node:test';
import assert from 'node:assert/strict';

function createMemoryStorage() {
  const values = new Map();

  return {
    getItem(key) {
      return values.has(key) ? values.get(key) : null;
    },
    setItem(key, value) {
      values.set(key, String(value));
    },
    removeItem(key) {
      values.delete(key);
    },
    clear() {
      values.clear();
    }
  };
}

globalThis.window = {
  chrome: null,
  location: {
    protocol: 'http:',
    hostname: 'localhost',
    port: '5000'
  },
  sessionStorage: createMemoryStorage(),
  localStorage: createMemoryStorage(),
  setTimeout,
  clearTimeout
};

const {
  default: inspectionController,
  createLightweightInspectionResult,
  getInlineResultImageBase64,
  loadImageUrlAsBase64,
  loadImageUrlAsBlob
} = await import(
  '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/inspection/inspectionController.js'
);
const { default: httpClient } = await import(
  '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/httpClient.js'
);
const { default: webMessageBridge } = await import(
  '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/webMessageBridge.js'
);

const INSPECTION_WEB_MESSAGE_TYPES = [
  'operatorExecuted',
  'stateChanged',
  'resultProduced',
  'progressChanged',
  'faulted',
  'inspectionCompleted',
  'progressNotification'
];

function createTextStreamResponse(chunks, options = {}) {
  const encodedChunks = chunks.map(chunk => new TextEncoder().encode(chunk));
  let index = 0;

  return {
    ok: true,
    body: {
      getReader() {
        return {
          async read() {
            if (index >= encodedChunks.length) {
              return { done: true };
            }

            const value = encodedChunks[index];
            index += 1;
            return { done: false, value };
          },
          releaseLock() {
            options.onRelease?.();
          }
        };
      }
    }
  };
}

function countInspectionWebMessageHandlers() {
  return INSPECTION_WEB_MESSAGE_TYPES.reduce(
    (total, type) => total + (webMessageBridge.messageHandlers.get(type)?.size ?? 0),
    0
  );
}

test('initializeWebMessage is idempotent and releases bridge handlers', (t) => {
  t.after(() => {
    inspectionController.initializeWebMessage();
  });

  inspectionController.initializeWebMessage();
  inspectionController.initializeWebMessage();

  assert.equal(countInspectionWebMessageHandlers(), INSPECTION_WEB_MESSAGE_TYPES.length);
  assert.equal(inspectionController.webMessageUnsubscribers.length, INSPECTION_WEB_MESSAGE_TYPES.length);

  inspectionController.disposeWebMessage();

  assert.equal(countInspectionWebMessageHandlers(), 0);
  assert.equal(inspectionController.webMessageUnsubscribers.length, 0);
  assert.equal(inspectionController.webMessageInitialized, false);

  inspectionController.initializeWebMessage();

  assert.equal(countInspectionWebMessageHandlers(), INSPECTION_WEB_MESSAGE_TYPES.length);
  assert.equal(inspectionController.webMessageInitialized, true);
});

test('webMessageBridge bounds pending request pressure and clears timeout handles', async (t) => {
  const originalMaxPendingRequests = webMessageBridge.maxPendingRequests;
  webMessageBridge.maxPendingRequests = 2;

  t.after(() => {
    webMessageBridge.clearPendingRequests();
    webMessageBridge.maxPendingRequests = originalMaxPendingRequests;
  });

  const first = webMessageBridge.sendMessage('pressure.test', { index: 1 }, true)
    .then(() => 'resolved', error => error.message);
  const second = webMessageBridge.sendMessage('pressure.test', { index: 2 }, true)
    .then(() => 'resolved', error => error.message);
  const third = webMessageBridge.sendMessage('pressure.test', { index: 3 }, true)
    .then(() => 'resolved', error => error.message);

  assert.match(await first, /Pending WebMessage request limit exceeded \(2\)/);
  assert.equal(webMessageBridge.pendingRequests.size, 2);
  assert.equal(webMessageBridge.pendingRequestTimeouts.size, 2);

  webMessageBridge.clearPendingRequests(new Error('test cleanup'));

  assert.equal(await second, 'test cleanup');
  assert.equal(await third, 'test cleanup');
  assert.equal(webMessageBridge.pendingRequests.size, 0);
  assert.equal(webMessageBridge.pendingRequestTimeouts.size, 0);
});

test('executeSingle encodes large Uint8Array images without expanding the whole payload', async (t) => {
  const originalPost = httpClient.post;
  const originalProjectId = inspectionController.projectId;
  const originalCameraId = inspectionController.cameraId;
  const originalFlowProvider = inspectionController.flowProvider;
  const originalBuffer = globalThis.Buffer;
  const originalBtoa = globalThis.btoa;
  const payloadBytes = new Uint8Array(150_000);

  for (let index = 0; index < payloadBytes.length; index += 1) {
    payloadBytes[index] = index % 251;
  }

  let capturedRequest = null;

  t.after(() => {
    httpClient.post = originalPost;
    inspectionController.projectId = originalProjectId;
    inspectionController.cameraId = originalCameraId;
    inspectionController.flowProvider = originalFlowProvider;
    globalThis.Buffer = originalBuffer;
    globalThis.btoa = originalBtoa;
  });

  globalThis.Buffer = undefined;
  if (typeof globalThis.btoa !== 'function') {
    globalThis.btoa = (value) => originalBuffer.from(value, 'binary').toString('base64');
  }

  httpClient.post = async (url, body) => {
    assert.equal(url, '/inspection/execute');
    capturedRequest = body;
    return {
      id: `chunked-input-${Date.now()}`,
      projectId: 'project-current',
      status: 'OK',
      imageId: 'image-1',
      outputImageBase64: 'AQID',
      outputData: {}
    };
  };

  inspectionController.setProject('project-current');
  inspectionController.setCamera(null);
  inspectionController.setFlowProvider(() => ({ operators: [], connections: [] }));

  const result = await inspectionController.executeSingle(payloadBytes);

  assert.equal(result.status, 'OK');
  assert.equal(capturedRequest.projectId, 'project-current');
  assert.deepEqual(capturedRequest.flowData, { operators: [], connections: [] });
  assert.equal(capturedRequest.imageBase64, originalBuffer.from(payloadBytes).toString('base64'));
});

test('createLightweightInspectionResult removes inline images while preserving metadata', () => {
  const original = {
    id: 'latest-result',
    status: 'OK',
    imageId: 'image-1',
    imageData: 'image-data',
    ImageData: 'image-data-upper',
    outputImage: 'output-image',
    OutputImage: 'output-image-upper',
    outputImageBase64: 'output-image-base64',
    OutputImageBase64: 'output-image-base64-upper',
    resultImageBase64: 'result-image-base64',
    ResultImageBase64: 'result-image-base64-upper',
    outputData: { Count: 1 }
  };

  assert.equal(getInlineResultImageBase64(original), 'image-data');

  const lightweight = createLightweightInspectionResult(original);

  assert.notEqual(lightweight, original);
  assert.equal(lightweight.id, 'latest-result');
  assert.equal(lightweight.status, 'OK');
  assert.equal(lightweight.imageId, 'image-1');
  assert.deepEqual(lightweight.outputData, { Count: 1 });
  assert.equal(lightweight.imageData, null);
  assert.equal(lightweight.ImageData, null);
  assert.equal(lightweight.outputImage, null);
  assert.equal(lightweight.OutputImage, null);
  assert.equal(lightweight.outputImageBase64, null);
  assert.equal(lightweight.OutputImageBase64, null);
  assert.equal(lightweight.resultImageBase64, null);
  assert.equal(lightweight.ResultImageBase64, null);
  assert.equal(original.outputImageBase64, 'output-image-base64');
});

test('createLightweightInspectionResult bounds output and analysis payload retention', () => {
  const longText = 'A'.repeat(900);
  const imagePayload = 'B'.repeat(512);
  const original = {
    id: 'large-result',
    status: 'OK',
    imageId: 'image-large',
    outputData: {
      Count: 1,
      Text: longText,
      OutputImageBase64: imagePayload,
      Items: Array.from({ length: 40 }, (_, index) => ({
        label: `item-${index}`,
        nested: {
          value: longText
        }
      }))
    },
    analysisData: {
      cards: Array.from({ length: 30 }, (_, index) => ({
        title: `card-${index}`,
        fields: [{ key: 'Text', value: longText }]
      }))
    },
    defects: Array.from({ length: 30 }, (_, index) => ({
      type: `defect-${index}`,
      confidenceScore: 0.9
    }))
  };

  const lightweight = createLightweightInspectionResult(original);

  assert.equal(lightweight.outputData.Text.length, 515);
  assert.match(lightweight.outputData.Text, /\.\.\.$/);
  assert.equal(lightweight.outputData.OutputImageBase64, undefined);
  assert.equal(lightweight.outputData.__omittedImageFieldCount, 1);
  assert.equal(lightweight.outputData.Items.length, 25);
  assert.equal(lightweight.outputData.Items.at(-1), '+16 more');
  assert.equal(lightweight.analysisData.cards.length, 25);
  assert.equal(lightweight.analysisData.cards.at(-1), '+6 more');
  assert.equal(lightweight.defects.length, 25);
  assert.equal(lightweight.defects.at(-1), '+6 more');
  assert.equal(JSON.stringify(lightweight).includes(longText), false);
  assert.equal(JSON.stringify(lightweight).includes(imagePayload), false);
  assert.equal(original.outputData.Text, longText);
  assert.equal(original.outputData.OutputImageBase64, imagePayload);
});

test('handleInspectionCompleted keeps last result lightweight and stores latest image separately', (t) => {
  const publishedImages = [];
  const completedResults = [];
  const unsubscribe = inspectionController.onInspectionCompleted(result => completedResults.push(result));

  inspectionController.setImageSinks([image => publishedImages.push(image)]);
  t.after(() => {
    inspectionController.setImageSinks([]);
    unsubscribe();
  });

  inspectionController.handleInspectionCompleted({
    id: `memory-result-${Date.now()}`,
    projectId: 'project-current',
    status: 'OK',
    imageId: 'image-1',
    outputImageBase64: 'BASE64_PAYLOAD',
    outputData: { Count: 1 }
  });

  const lastResult = inspectionController.getLastResult();

  assert.equal(lastResult.imageId, 'image-1');
  assert.deepEqual(lastResult.outputData, { Count: 1 });
  assert.equal(lastResult.imageData, null);
  assert.equal(lastResult.outputImageBase64, null);
  assert.equal(inspectionController.getLastResultImageBase64(), 'BASE64_PAYLOAD');
  assert.deepEqual(publishedImages, ['data:image/png;base64,BASE64_PAYLOAD']);
  assert.equal(completedResults.length, 1);
  assert.equal(completedResults[0].outputImageBase64, 'BASE64_PAYLOAD');
});

test('handleResultEvent fetches cached image with authorization and publishes a Blob', async (t) => {
  const publishedImages = [];
  const imageId = '00000000-0000-0000-0000-000000000123';
  const expectedUrl = `http://localhost:5000/api/images/${imageId}`;
  const completedResults = [];
  const originalFetch = globalThis.fetch;
  const originalHeaders = httpClient.defaultHeaders;
  const unsubscribe = inspectionController.onInspectionCompleted(result => completedResults.push(result));
  const expectedBlob = new Blob([new Uint8Array([1, 2, 3])], { type: 'image/png' });

  inspectionController.setImageSinks([image => publishedImages.push(image)]);
  t.after(() => {
    globalThis.fetch = originalFetch;
    httpClient.defaultHeaders = originalHeaders;
    inspectionController.cancelLastResultImageLoad();
    inspectionController.lastResultImageBlob = null;
    inspectionController.lastResultImageUrl = null;
    inspectionController.setImageSinks([]);
    unsubscribe();
  });

  httpClient.defaultHeaders = {
    ...originalHeaders,
    Authorization: 'Bearer image-token'
  };
  globalThis.fetch = async (url, options) => {
    assert.equal(url, expectedUrl);
    assert.equal(options.method, 'GET');
    assert.equal(options.headers.Authorization, 'Bearer image-token');
    return {
      ok: true,
      status: 200,
      async blob() {
        return expectedBlob;
      }
    };
  };

  inspectionController.handleResultEvent({
    resultId: `url-result-${Date.now()}`,
    projectId: 'project-current',
    imageId,
    status: 'OK',
    outputData: { Count: 2 }
  });

  await inspectionController.ensureLastResultImageLoaded();

  const lastResult = inspectionController.getLastResult();

  assert.equal(lastResult.imageId, imageId);
  assert.equal(lastResult.outputImageBase64, null);
  assert.equal(inspectionController.getLastResultImageBase64(), null);
  assert.equal(inspectionController.getLastResultImageUrl(), expectedUrl);
  assert.equal(inspectionController.getLastResultImageBlob(), expectedBlob);
  assert.deepEqual(publishedImages, [expectedBlob]);
  assert.equal(completedResults.length, 1);
  assert.equal(completedResults[0].imageId, imageId);
  assert.equal(completedResults[0].outputImageBase64, undefined);
});

test('cached inspection image failure is visible and retry publishes the recovered Blob', async (t) => {
  const originalFetch = globalThis.fetch;
  const originalHeaders = httpClient.defaultHeaders;
  const publishedImages = [];
  const states = [];
  const recoveredBlob = new Blob([new Uint8Array([4, 5, 6])], { type: 'image/png' });
  const unsubscribeState = inspectionController.onInspectionImageState(state => states.push(state));
  let requests = 0;

  inspectionController.setImageSinks([image => publishedImages.push(image)]);
  t.after(() => {
    globalThis.fetch = originalFetch;
    httpClient.defaultHeaders = originalHeaders;
    inspectionController.cancelLastResultImageLoad();
    inspectionController.lastResultImageBlob = null;
    inspectionController.lastResultImageUrl = null;
    inspectionController.setImageSinks([]);
    unsubscribeState();
  });

  httpClient.defaultHeaders = {
    ...originalHeaders,
    Authorization: 'Bearer retry-token'
  };
  globalThis.fetch = async (_url, options) => {
    assert.equal(options.headers.Authorization, 'Bearer retry-token');
    requests += 1;
    if (requests === 1) {
      return { ok: false, status: 401 };
    }

    return {
      ok: true,
      status: 200,
      async blob() {
        return recoveredBlob;
      }
    };
  };

  inspectionController.handleResultEvent({
    resultId: `retry-result-${Date.now()}`,
    projectId: 'project-current',
    imageId: '00000000-0000-0000-0000-000000000456',
    status: 'OK'
  });
  await inspectionController.ensureLastResultImageLoaded();

  assert.equal(inspectionController.getLastResultImageState().status, 'error');
  assert.match(inspectionController.getLastResultImageState().message, /HTTP 401/);
  assert.deepEqual(publishedImages, []);

  await inspectionController.retryLastResultImage();

  assert.equal(requests, 2);
  assert.equal(inspectionController.getLastResultImageState().status, 'ready');
  assert.deepEqual(publishedImages, [recoveredBlob]);
  assert.equal(states.some(state => state.status === 'error'), true);
});

test('missing cached inspection image exposes a 404 load error', async (t) => {
  const originalFetch = globalThis.fetch;
  const originalHeaders = httpClient.defaultHeaders;
  const states = [];
  const unsubscribeState = inspectionController.onInspectionImageState(state => states.push(state));

  t.after(() => {
    globalThis.fetch = originalFetch;
    httpClient.defaultHeaders = originalHeaders;
    inspectionController.cancelLastResultImageLoad();
    inspectionController.lastResultImageBlob = null;
    inspectionController.lastResultImageUrl = null;
    unsubscribeState();
  });

  httpClient.defaultHeaders = {
    ...originalHeaders,
    Authorization: 'Bearer missing-image-token'
  };
  globalThis.fetch = async (_url, options) => {
    assert.equal(options.headers.Authorization, 'Bearer missing-image-token');
    return { ok: false, status: 404 };
  };

  inspectionController.handleResultEvent({
    resultId: `missing-image-${Date.now()}`,
    projectId: 'project-current',
    imageId: '00000000-0000-0000-0000-000000000457',
    status: 'OK'
  });
  await inspectionController.ensureLastResultImageLoaded();

  assert.equal(inspectionController.getLastResultImageState().status, 'error');
  assert.match(inspectionController.getLastResultImageState().message, /HTTP 404/);
  assert.equal(states.some(state => state.status === 'error'), true);
});

test('stale cached image loads cannot replace the newest result image', async (t) => {
  const originalFetch = globalThis.fetch;
  const originalHeaders = httpClient.defaultHeaders;
  const publishedImages = [];
  const pendingRequests = [];
  const firstBlob = new Blob([new Uint8Array([7])], { type: 'image/png' });
  const secondBlob = new Blob([new Uint8Array([8])], { type: 'image/png' });

  inspectionController.setImageSinks([image => publishedImages.push(image)]);
  t.after(() => {
    globalThis.fetch = originalFetch;
    httpClient.defaultHeaders = originalHeaders;
    inspectionController.cancelLastResultImageLoad();
    inspectionController.lastResultImageBlob = null;
    inspectionController.lastResultImageUrl = null;
    inspectionController.setImageSinks([]);
  });

  httpClient.defaultHeaders = {
    ...originalHeaders,
    Authorization: 'Bearer stale-token'
  };
  globalThis.fetch = (_url, options) => new Promise(resolve => {
    pendingRequests.push({ resolve, signal: options.signal });
  });

  inspectionController.handleResultEvent({
    resultId: `stale-first-${Date.now()}`,
    projectId: 'project-current',
    imageId: '00000000-0000-0000-0000-000000000701',
    status: 'OK'
  });
  inspectionController.handleResultEvent({
    resultId: `stale-second-${Date.now()}`,
    projectId: 'project-current',
    imageId: '00000000-0000-0000-0000-000000000702',
    status: 'OK'
  });

  assert.equal(pendingRequests.length, 2);
  assert.equal(pendingRequests[0].signal.aborted, true);
  pendingRequests[1].resolve({
    ok: true,
    status: 200,
    async blob() {
      return secondBlob;
    }
  });
  await inspectionController.ensureLastResultImageLoaded();

  pendingRequests[0].resolve({
    ok: true,
    status: 200,
    async blob() {
      return firstBlob;
    }
  });
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.equal(inspectionController.getLastResultImageBlob(), secondBlob);
  assert.deepEqual(publishedImages, [secondBlob]);
});

test('duplicate result ids do not republish images or callbacks', (t) => {
  const publishedImages = [];
  const completedResults = [];
  const unsubscribe = inspectionController.onInspectionCompleted(result => completedResults.push(result));
  const resultId = `duplicate-result-${Date.now()}`;

  inspectionController.recentCompletedResultKeys.clear();
  inspectionController.setImageSinks([image => publishedImages.push(image)]);
  t.after(() => {
    inspectionController.recentCompletedResultKeys.clear();
    inspectionController.setImageSinks([]);
    unsubscribe();
  });

  inspectionController.handleResultEvent({
    resultId,
    projectId: 'project-current',
    status: 'OK',
    outputImageBase64: 'FIRST_IMAGE',
    outputData: { Count: 1 }
  });
  inspectionController.handleResultEvent({
    resultId,
    projectId: 'project-current',
    status: 'OK',
    outputImageBase64: 'SECOND_IMAGE',
    outputData: { Count: 2 }
  });

  assert.deepEqual(publishedImages, ['data:image/png;base64,FIRST_IMAGE']);
  assert.equal(completedResults.length, 1);
  assert.equal(completedResults[0].outputImageBase64, 'FIRST_IMAGE');
  assert.equal(inspectionController.getLastResultImageBase64(), 'FIRST_IMAGE');
});

test('stopRealtime publishes stopped state for the current project when terminal SSE is missing', async (t) => {
  const originalPost = httpClient.post;
  const originalProjectId = inspectionController.projectId;
  const originalAbortController = inspectionController.abortController;
  const originalEventSource = inspectionController.eventSource;
  const states = [];
  const unsubscribe = inspectionController.subscribeState(state => states.push(state));
  let postedBody = null;
  let aborted = false;

  t.after(() => {
    unsubscribe();
    httpClient.post = originalPost;
    inspectionController.projectId = originalProjectId;
    inspectionController.abortController = originalAbortController;
    inspectionController.eventSource = originalEventSource;
  });

  httpClient.post = async (url, body) => {
    assert.equal(url, '/inspection/realtime/stop');
    postedBody = body;
    return {};
  };
  inspectionController.setProject('project-stop-terminal');
  inspectionController.abortController = { abort() { aborted = true; } };
  inspectionController.applyRuntimeStateSnapshot({
    projectId: 'project-stop-terminal',
    status: 'Running',
    isBusy: true
  });

  await inspectionController.stopRealtime();

  const lastState = states.at(-1);
  assert.deepEqual(postedBody, { projectId: 'project-stop-terminal' });
  assert.equal(aborted, true);
  assert.equal(lastState.projectId, 'project-stop-terminal');
  assert.equal(lastState.status, 'idle');
  assert.equal(lastState.isRunning, false);
  assert.equal(lastState.isRealtime, false);
});

test('recent completed result dedupe keys are hard bounded for high-frequency streams', (t) => {
  const originalMaxEntries = inspectionController.resultDedupeMaxEntries;
  const originalWindowMs = inspectionController.resultDedupeWindowMs;
  inspectionController.recentCompletedResultKeys.clear();
  inspectionController.resultDedupeMaxEntries = 3;
  inspectionController.resultDedupeWindowMs = 60_000;

  t.after(() => {
    inspectionController.recentCompletedResultKeys.clear();
    inspectionController.resultDedupeMaxEntries = originalMaxEntries;
    inspectionController.resultDedupeWindowMs = originalWindowMs;
  });

  assert.equal(inspectionController.markResultAsHandled({ id: 'r1' }), true);
  assert.equal(inspectionController.markResultAsHandled({ id: 'r2' }), true);
  assert.equal(inspectionController.markResultAsHandled({ id: 'r3' }), true);
  assert.equal(inspectionController.markResultAsHandled({ id: 'r4' }), true);

  assert.equal(inspectionController.recentCompletedResultKeys.size, 3);
  assert.equal(inspectionController.recentCompletedResultKeys.has('r1'), false);
  assert.equal(inspectionController.recentCompletedResultKeys.has('r2'), true);
  assert.equal(inspectionController.recentCompletedResultKeys.has('r3'), true);
  assert.equal(inspectionController.recentCompletedResultKeys.has('r4'), true);
  assert.equal(inspectionController.markResultAsHandled({ id: 'r4' }), false);
});

test('openSseStream drops oversized SSE frames before dispatching', async (t) => {
  const originalFetch = globalThis.fetch;
  const originalDispatchSseFrame = inspectionController.dispatchSseFrame;
  const originalMaxFrameChars = inspectionController.sseMaxFrameChars;
  const originalMaxBufferChars = inspectionController.sseMaxBufferChars;
  const originalUseSse = inspectionController.useSse;
  const originalWarn = console.warn;
  const dispatchedFrames = [];
  const warnings = [];

  t.after(() => {
    globalThis.fetch = originalFetch;
    inspectionController.dispatchSseFrame = originalDispatchSseFrame;
    inspectionController.sseMaxFrameChars = originalMaxFrameChars;
    inspectionController.sseMaxBufferChars = originalMaxBufferChars;
    inspectionController.useSse = originalUseSse;
    console.warn = originalWarn;
  });

  inspectionController.sseMaxFrameChars = 64;
  inspectionController.sseMaxBufferChars = 256;
  inspectionController.dispatchSseFrame = frame => dispatchedFrames.push(frame);
  console.warn = (...args) => warnings.push(args);
  globalThis.fetch = async () => createTextStreamResponse([
    `event: resultProduced\ndata: ${'x'.repeat(96)}\n\n`,
    'event: heartbeat\ndata: {}\n\n'
  ]);

  await inspectionController.openSseStream(
    'http://localhost:5000/api/inspection/realtime/project/events',
    null,
    new AbortController().signal
  );

  assert.deepEqual(dispatchedFrames, ['event: heartbeat\ndata: {}']);
  assert.equal(warnings.length, 1);
  assert.equal(inspectionController.useSse, false);
});

test('openSseStream fails and releases the reader when SSE buffer has no frame boundary', async (t) => {
  const originalFetch = globalThis.fetch;
  const originalMaxFrameChars = inspectionController.sseMaxFrameChars;
  const originalMaxBufferChars = inspectionController.sseMaxBufferChars;
  const originalUseSse = inspectionController.useSse;
  let releaseCalled = false;

  t.after(() => {
    globalThis.fetch = originalFetch;
    inspectionController.sseMaxFrameChars = originalMaxFrameChars;
    inspectionController.sseMaxBufferChars = originalMaxBufferChars;
    inspectionController.useSse = originalUseSse;
  });

  inspectionController.sseMaxFrameChars = 256;
  inspectionController.sseMaxBufferChars = 16;
  globalThis.fetch = async () => createTextStreamResponse(
    ['data: this frame never terminates'],
    { onRelease: () => { releaseCalled = true; } }
  );

  await assert.rejects(
    () => inspectionController.openSseStream(
      'http://localhost:5000/api/inspection/realtime/project/events',
      null,
      new AbortController().signal
    ),
    /SSE buffer exceeded 16 characters/
  );

  assert.equal(releaseCalled, true);
  assert.equal(inspectionController.useSse, false);
});

test('loadImageUrlAsBase64 converts cached image URLs on demand', async (t) => {
  const originalFetch = globalThis.fetch;
  t.after(() => {
    globalThis.fetch = originalFetch;
  });

  globalThis.fetch = async (url, options) => {
    assert.equal(url, 'http://localhost:5000/api/images/image-1');
    assert.equal(options.method, 'GET');
    return {
      ok: true,
      headers: {
        get(name) {
          return name.toLowerCase() === 'content-length' ? '3' : null;
        }
      },
      async blob() {
        return new Blob([new Uint8Array([1, 2, 3])], { type: 'image/png' });
      }
    };
  };

  assert.equal(await loadImageUrlAsBase64('http://localhost:5000/api/images/image-1'), 'AQID');
});

test('loadImageUrlAsBase64 skips cached images above the preview input budget', async (t) => {
  const originalFetch = globalThis.fetch;
  let blobRequested = false;
  t.after(() => {
    globalThis.fetch = originalFetch;
  });

  globalThis.fetch = async () => ({
    ok: true,
    headers: {
      get(name) {
        return name.toLowerCase() === 'content-length' ? '5' : null;
      }
    },
    async blob() {
      blobRequested = true;
      return new Blob([new Uint8Array([1, 2, 3, 4, 5])], { type: 'image/png' });
    }
  });

  assert.equal(await loadImageUrlAsBase64('/images/image-large', { maxBytes: 4 }), null);
  assert.equal(blobRequested, false);
});
