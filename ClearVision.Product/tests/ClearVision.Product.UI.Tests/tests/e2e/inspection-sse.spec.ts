import { test, expect } from '@playwright/test';

async function openModuleHost(page) {
  await page.route('**/module-host.html', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'text/html',
      body: '<!doctype html><html><head><meta charset="utf-8"><title>Module Host</title></head><body></body></html>',
    });
  });

  await page.goto('/module-host.html');
}

test('inspection SSE stores event ids and sends Last-Event-ID on reconnect', async ({ page }) => {
  await openModuleHost(page);

  const result = await page.evaluate(async () => {
    sessionStorage.setItem('cv_auth_token', 'sse-token');
    const inspectionModule = await import('/src/features/inspection/inspectionController.js');
    const controller = inspectionModule.default;
    controller.unsubscribeFromSseEvents();
    controller.lastSseEventId = null;
    controller.sseProjectId = 'project-sse';

    const requests = [];
    const encoder = new TextEncoder();
    window.fetch = async (url, options = {}) => {
      requests.push({
        url: String(url),
        headers: Object.fromEntries(new Headers(options.headers).entries()),
      });

      return new Response(new ReadableStream({
        start(streamController) {
          streamController.enqueue(encoder.encode('id: 41\nevent: progressChanged\ndata: {"processedCount":1}\n\n'));
          streamController.close();
        },
      }), {
        status: 200,
        headers: { 'Content-Type': 'text/event-stream' },
      });
    };

    await controller.openSseStream('/events', 'sse-token', new AbortController().signal);
    await controller.openSseStream('/events', 'sse-token', new AbortController().signal);

    return {
      lastSseEventId: controller.lastSseEventId,
      requests,
    };
  });

  expect(result.lastSseEventId).toBe('41');
  expect(result.requests[0].headers.authorization).toBe('Bearer sse-token');
  expect(result.requests[0].headers['last-event-id']).toBeUndefined();
  expect(result.requests[1].headers['last-event-id']).toBe('41');
});

test('inspection SSE reconnects after stream EOF with the last event id', async ({ page }) => {
  await openModuleHost(page);

  const result = await page.evaluate(async () => {
    sessionStorage.setItem('cv_auth_token', 'sse-token');
    const inspectionModule = await import('/src/features/inspection/inspectionController.js');
    const controller = inspectionModule.default;
    controller.unsubscribeFromSseEvents();
    controller.lastSseEventId = null;
    controller.sseProjectId = null;
    controller.sseReconnectBaseDelayMs = 0;
    controller.sseReconnectMaxDelayMs = 0;

    const requests = [];
    const encoder = new TextEncoder();
    let streamCount = 0;
    window.fetch = async (url, options = {}) => {
      const requestUrl = String(url);
      if (!requestUrl.includes('/inspection/realtime/project-sse/events')) {
        throw new Error(`Unexpected request: ${requestUrl}`);
      }

      streamCount += 1;
      requests.push({
        url: requestUrl,
        headers: Object.fromEntries(new Headers(options.headers).entries()),
      });

      return new Response(new ReadableStream({
        start(streamController) {
          streamController.enqueue(encoder.encode(`id: ${40 + streamCount}\nevent: progressChanged\ndata: {"processedCount":${streamCount}}\n\n`));
          if (streamCount === 1) {
            streamController.close();
          }
        },
      }), {
        status: 200,
        headers: { 'Content-Type': 'text/event-stream' },
      });
    };

    controller.subscribeToSseEvents('project-sse');
    await new Promise((resolve, reject) => {
      const deadline = window.setTimeout(() => reject(new Error('Timed out waiting for SSE reconnect')), 1000);
      const check = () => {
        if (requests.length >= 2 && controller.lastSseEventId === '42') {
          window.clearTimeout(deadline);
          resolve(undefined);
          return;
        }

        window.setTimeout(check, 10);
      };

      check();
    });
    controller.unsubscribeFromSseEvents();

    return {
      lastSseEventId: controller.lastSseEventId,
      requests,
      closedConnection: controller.eventSource === null && controller.useSse === false,
    };
  });

  expect(result.lastSseEventId).toBe('42');
  expect(result.requests[0].headers['last-event-id']).toBeUndefined();
  expect(result.requests[1].headers['last-event-id']).toBe('41');
  expect(result.closedConnection).toBe(true);
});

test('inspection realtime aborts SSE when stop or start failure occurs', async ({ page }) => {
  await openModuleHost(page);

  const result = await page.evaluate(async () => {
    const inspectionModule = await import('/src/features/inspection/inspectionController.js');
    const controller = inspectionModule.default;
    controller.unsubscribeFromSseEvents();
    controller.setProject('project-sse');
    controller.setCamera('camera-1');

    let stopAbortObserved = false;
    window.fetch = async (url, options = {}) => {
      const requestUrl = String(url);
      if (requestUrl.includes('/inspection/realtime/project-sse/events')) {
        options.signal?.addEventListener('abort', () => {
          stopAbortObserved = true;
        });
        return new Promise(() => {});
      }

      if (requestUrl.includes('/inspection/realtime/stop')) {
        return new Response('{}', {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }

      throw new Error(`Unexpected request: ${requestUrl}`);
    };

    controller.subscribeToSseEvents('project-sse');
    await controller.stopRealtime();
    const stopClosedConnection = controller.eventSource === null && controller.useSse === false;

    let startFailureAbortObserved = false;
    window.fetch = async (url, options = {}) => {
      const requestUrl = String(url);
      if (requestUrl.includes('/inspection/realtime/project-sse/events')) {
        options.signal?.addEventListener('abort', () => {
          startFailureAbortObserved = true;
        });
        return new Promise(() => {});
      }

      if (requestUrl.includes('/inspection/realtime/start')) {
        return new Response('start failed', { status: 500 });
      }

      throw new Error(`Unexpected request: ${requestUrl}`);
    };

    let startFailed = false;
    try {
      await controller.startRealtime();
    } catch {
      startFailed = true;
    }

    return {
      stopAbortObserved,
      stopClosedConnection,
      startFailed,
      startFailureAbortObserved,
      startClosedConnection: controller.eventSource === null && controller.useSse === false,
    };
  });

  expect(result.stopAbortObserved).toBe(true);
  expect(result.stopClosedConnection).toBe(true);
  expect(result.startFailed).toBe(true);
  expect(result.startFailureAbortObserved).toBe(true);
  expect(result.startClosedConnection).toBe(true);
});

test('inspection result fetches a protected image as a Blob before drawing it', async ({ page }) => {
  await openModuleHost(page);

  const result = await page.evaluate(async () => {
    document.body.innerHTML = '<div id="inspection-test-viewer" style="width: 640px; height: 480px"></div>';

    const inspectionModule = await import('/src/features/inspection/inspectionController.js');
    const imageViewerModule = await import('/src/features/image-viewer/imageViewer.js');
    const httpClientModule = await import('/src/core/messaging/httpClient.js');
    const controller = inspectionModule.default;
    const httpClient = httpClientModule.default;
    const viewer = new imageViewerModule.ImageViewerComponent('inspection-test-viewer');
    const originalFetch = window.fetch;
    const originalHeaders = httpClient.defaultHeaders;
    const requests: Array<{ url: string; authorization: string | null }> = [];
    const png = Uint8Array.from(
      atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLJ4QAAAABJRU5ErkJggg=='),
      value => value.charCodeAt(0),
    );

    try {
      httpClient.defaultHeaders = {
        ...originalHeaders,
        Authorization: 'Bearer browser-image-token',
      };
      window.fetch = async (url, options = {}) => {
        requests.push({
          url: String(url),
          authorization: new Headers(options.headers).get('authorization'),
        });
        return new Response(png, {
          status: 200,
          headers: { 'Content-Type': 'image/png' },
        });
      };

      controller.recentCompletedResultKeys.clear();
      controller.setImageSinks([source => viewer.loadImage(source, { silent: true })]);
      controller.handleResultEvent({
        resultId: `protected-image-${Date.now()}`,
        projectId: 'project-protected-image',
        imageId: '00000000-0000-0000-0000-000000000888',
        status: 'OK',
      });
      await controller.ensureLastResultImageLoaded();

      for (let attempt = 0; attempt < 20 && !viewer.currentImage; attempt += 1) {
        await new Promise(resolve => window.setTimeout(resolve, 10));
      }

      return {
        requests,
        imageIsBlob: controller.getLastResultImageBlob() instanceof Blob,
        imageWidth: viewer.currentImage?.width ?? 0,
        imageHeight: viewer.currentImage?.height ?? 0,
      };
    } finally {
      window.fetch = originalFetch;
      httpClient.defaultHeaders = originalHeaders;
      controller.cancelLastResultImageLoad();
      controller.setImageSinks([]);
      controller.recentCompletedResultKeys.clear();
      viewer.destroy();
    }
  });

  expect(result.requests).toHaveLength(1);
  expect(result.requests[0].url).toContain('/api/images/00000000-0000-0000-0000-000000000888');
  expect(result.requests[0].authorization).toBe('Bearer browser-image-token');
  expect(result.imageIsBlob).toBe(true);
  expect(result.imageWidth).toBe(1);
  expect(result.imageHeight).toBe(1);
});
