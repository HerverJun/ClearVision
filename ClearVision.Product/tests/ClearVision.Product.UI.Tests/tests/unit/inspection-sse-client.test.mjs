import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildSseHeaders,
  buildSseUrl,
  parseSseFrame
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/inspection/inspectionSseClient.mjs';

test('buildSseHeaders includes authorization and last event id when available', () => {
  assert.deepEqual(buildSseHeaders('token-1', '42'), {
    Authorization: 'Bearer token-1',
    'Last-Event-ID': '42'
  });

  assert.deepEqual(buildSseHeaders(null, null), {});
});

test('buildSseUrl appends lastEventId cursor without dropping existing query', () => {
  assert.equal(
    buildSseUrl('http://localhost:5000/api/inspection/realtime/project/events', '42'),
    'http://localhost:5000/api/inspection/realtime/project/events?lastEventId=42'
  );

  assert.equal(
    buildSseUrl('http://localhost:5000/api/stations/events?stationId=s1', '7'),
    'http://localhost:5000/api/stations/events?stationId=s1&lastEventId=7'
  );

  assert.equal(buildSseUrl('/events', null), '/events');
});

test('parseSseFrame parses event id, event name, comments, and multiline data', () => {
  const parsed = parseSseFrame([
    ':keepalive',
    'id: 42',
    'event: progressChanged',
    'data: {"processedCount":',
    'data: 3}'
  ].join('\n'));

  assert.deepEqual(parsed, {
    eventName: 'progressChanged',
    eventId: '42',
    payload: { processedCount: 3 }
  });
});

test('parseSseFrame ignores heartbeat-only frames', () => {
  assert.equal(parseSseFrame(':keepalive\n\n'), null);
});
