import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';

async function openModuleHost(page) {
  await page.route('**/module-host.html', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'text/html',
      body: '<!doctype html><html><head><meta charset="utf-8"><title>Wave 2E module host</title></head><body></body></html>',
    });
  });

  await page.goto('/module-host.html');
}

test('Wave 2E browser client recovers an idempotent request from a stale port without losing request state', async ({ page }) => {
  await openModuleHost(page);

  const result = await page.evaluate(async () => {
    const { HttpClient } = await import('/src/core/messaging/httpClient.js');
    const originalFetch = window.fetch;
    const previousToken = sessionStorage.getItem('cv_auth_token');
    const requests = [];
    const controller = new AbortController();
    sessionStorage.setItem('cv_auth_token', 'wave2e-browser-token');

    window.fetch = async (url, options = {}) => {
      const request = {
        url: String(url),
        method: options.method,
        body: options.body,
        headers: Object.fromEntries(new Headers(options.headers).entries()),
        preservesAbortSignal: options.signal === controller.signal,
      };
      requests.push(request);

      if (request.url === 'http://localhost:5001/api/recovery/station-command') {
        const error = new TypeError('net::ERR_CONNECTION_REFUSED');
        Object.defineProperty(error, 'cause', { value: { code: 'ECONNREFUSED' } });
        throw error;
      }

      if (request.url === 'http://localhost:5000/health') {
        return new Response('ok', { status: 200 });
      }

      if (request.url === 'http://localhost:5000/api/recovery/station-command') {
        return new Response(JSON.stringify({ accepted: true }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }

      return new Response('not found', { status: 404 });
    };

    try {
      const client = new HttpClient('http://localhost:5001/api');
      const response = await client.put(
        '/recovery/station-command',
        { commandId: 'wave2e-command' },
        {
          headers: { 'X-Wave2E': 'port-recovery' },
          idempotencyKey: 'wave2e-port-recovery-1',
          signal: controller.signal,
        },
      );

      return { response, requests };
    } finally {
      window.fetch = originalFetch;
      if (previousToken === null) {
        sessionStorage.removeItem('cv_auth_token');
      } else {
        sessionStorage.setItem('cv_auth_token', previousToken);
      }
    }
  });

  expect(result.response).toEqual({ accepted: true });
  expect(result.requests.map(request => `${request.method} ${request.url}`)).toEqual([
    'PUT http://localhost:5001/api/recovery/station-command',
    'GET http://localhost:5000/health',
    'PUT http://localhost:5000/api/recovery/station-command',
  ]);

  const retry = result.requests[2];
  expect(retry.body).toBe(JSON.stringify({ commandId: 'wave2e-command' }));
  expect(retry.headers.authorization).toBe('Bearer wave2e-browser-token');
  expect(retry.headers['x-wave2e']).toBe('port-recovery');
  expect(retry.headers['idempotency-key']).toBe('wave2e-port-recovery-1');
  expect(retry.preservesAbortSignal).toBe(true);
});

test('Wave 2E browser client preserves the stable analysis query-budget 400 contract', async ({ page }) => {
  await openModuleHost(page);
  await page.route('**/api/analysis/report/**', async route => {
    await route.fulfill({
      status: 400,
      contentType: 'application/json',
      body: JSON.stringify({
        error: 'ANALYSIS_TIME_RANGE_LIMIT',
        message: 'Analysis requests may span at most 31 days.',
        maximumWindowDays: 31,
        maximumTrendPoints: 745,
        maximumTrendRows: 25000,
      }),
    });
  });

  const result = await page.evaluate(async () => {
    const { HttpClient, HttpError } = await import('/src/core/messaging/httpClient.js');
    const client = new HttpClient(`${window.location.origin}/api`);

    try {
      await client.get('/analysis/report/wave2e-project', {
        startTime: '2026-07-01T00:00:00Z',
        endTime: '2026-08-02T00:00:00Z',
      });
      return { unexpectedlySucceeded: true };
    } catch (error) {
      return {
        isHttpError: error instanceof HttpError,
        status: error?.status,
        message: error?.message,
        payload: error?.payload,
      };
    }
  });

  expect(result.unexpectedlySucceeded).toBeUndefined();
  expect(result.isHttpError).toBe(true);
  expect(result.status).toBe(400);
  expect(result.message).toBe('ANALYSIS_TIME_RANGE_LIMIT');
  expect(result.payload).toEqual({
    error: 'ANALYSIS_TIME_RANGE_LIMIT',
    message: 'Analysis requests may span at most 31 days.',
    maximumWindowDays: 31,
    maximumTrendPoints: 745,
    maximumTrendRows: 25000,
  });
});

test('Wave 2E Station command SSE reconnects with Last-Event-ID and never regresses a terminal command', async ({ page }) => {
  await openModuleHost(page);

  const result = await page.evaluate(async () => {
    const { StationMonitorView } = await import('/src/features/stations/stationMonitorView.js');
    document.body.innerHTML = '<div id="wave2e-station-host"></div>';
    const view = new StationMonitorView('wave2e-station-host');
    const originalFetch = window.fetch;
    const requests = [];
    const encoder = new TextEncoder();
    let streamCount = 0;

    view.canReadSensitiveMonitoring = () => true;
    view.selectedStationId = 'station-wave2e';
    view.selectedStationDetail = {
      stationId: 'station-wave2e',
      recentCommands: [],
    };
    view.sseReconnectBaseDelayMs = 5;
    view.sseReconnectMaxDelayMs = 5;
    view.isActive = true;

    const delivered = {
      commandId: 'command-wave2e',
      stationId: 'station-wave2e',
      status: 'Delivered',
      progressPercent: 10,
      deliveredAtUtc: '2026-08-31T00:00:01Z',
    };
    const succeeded = {
      ...delivered,
      status: 'Succeeded',
      progressPercent: 100,
      completedAtUtc: '2026-08-31T00:00:03Z',
    };

    window.fetch = async (url, options = {}) => {
      streamCount += 1;
      requests.push({
        url: String(url),
        headers: Object.fromEntries(new Headers(options.headers).entries()),
      });

      const frames = streamCount === 1
        ? `id: 101\nevent: stationCommandUpdated\ndata: ${JSON.stringify(delivered)}\n\n`
        : [
          `id: 102\nevent: stationCommandUpdated\ndata: ${JSON.stringify(succeeded)}\n\n`,
          `id: 103\nevent: stationCommandUpdated\ndata: ${JSON.stringify(delivered)}\n\n`,
        ].join('');

      return new Response(new ReadableStream({
        start(streamController) {
          streamController.enqueue(encoder.encode(frames));
          streamController.close();
        },
      }), {
        status: 200,
        headers: { 'Content-Type': 'text/event-stream' },
      });
    };

    try {
      view.connectSse();
      await new Promise((resolve, reject) => {
        const deadline = window.setTimeout(() => reject(new Error('Timed out waiting for Station SSE reconnect')), 1000);
        const check = () => {
          const command = view.selectedStationDetail?.recentCommands?.[0];
          if (requests.length >= 2 && view.lastSseEventId === '103' && command?.status === 'Succeeded') {
            window.clearTimeout(deadline);
            resolve(undefined);
            return;
          }

          window.setTimeout(check, 5);
        };

        check();
      });

      return {
        lastSseEventId: view.lastSseEventId,
        requests,
        commands: view.selectedStationDetail?.recentCommands ?? [],
      };
    } finally {
      view.dispose();
      window.fetch = originalFetch;
    }
  });

  expect(result.requests).toHaveLength(2);
  expect(result.requests[0].headers['last-event-id']).toBeUndefined();
  expect(result.requests[1].headers['last-event-id']).toBe('101');
  expect(result.lastSseEventId).toBe('103');
  expect(result.commands).toHaveLength(1);
  expect(result.commands[0]).toMatchObject({
    commandId: 'command-wave2e',
    status: 'Succeeded',
    progressPercent: 100,
  });
});

test('Wave 2E Station CSV export downloads .csv and neutralizes Station-controlled formulas', async ({ page }) => {
  await openModuleHost(page);

  await page.evaluate(async () => {
    const { StationMonitorView } = await import('/src/features/stations/stationMonitorView.js');
    document.body.innerHTML = '<div id="wave2e-csv-host"></div>';
    const view = new StationMonitorView('wave2e-csv-host');
    view.monitorResults = [{
      stationId: 'station-wave2e',
      stationLabel: '工站 Wave 2E',
      sequenceId: 1,
      status: 'NG',
      diagnosticCode: ' \t=CMD()',
      diagnosticMessage: '\r\n@HYPERLINK("https://example.test")',
      executionTimeMs: 12,
      completedAtUtc: '2026-08-31T00:00:00Z',
      packageName: '正常包名',
    }];
    (window as any).__wave2eStationView = view;
  });

  const downloadPromise = page.waitForEvent('download');
  await page.evaluate(() => (window as any).__wave2eStationView.exportMonitorResults('csv'));
  const download = await downloadPromise;
  const downloadPath = await download.path();

  expect(download.suggestedFilename()).toMatch(/^station-results-\d+\.csv$/);
  expect(downloadPath).not.toBeNull();
  const csv = await readFile(downloadPath!, 'utf8');
  expect(csv).toContain("' \t=CMD()");
  expect(csv).toContain("'\r\n@HYPERLINK");
  expect(csv).toContain('正常包名');

  await page.evaluate(() => {
    (window as any).__wave2eStationView.dispose();
    delete (window as any).__wave2eStationView;
  });
});
