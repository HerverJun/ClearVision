import { describe, expect, it, vi } from 'vitest';
import type { ApiTransport } from '@/platform/api';
import {
  StationContractDecodeError,
  createStationSseAdapter,
  type StationSseEvent
} from '@/capabilities/stations-read';
import { stationStatus, stationSummary } from './stationFixtures';

function streamOf(...chunks: string[]): ReadableStream<Uint8Array> {
  const encoder = new TextEncoder();
  return new ReadableStream({
    start(controller) {
      chunks.forEach(chunk => controller.enqueue(encoder.encode(chunk)));
      controller.close();
    }
  });
}

function apiFor(stream: ReadableStream<Uint8Array>): ApiTransport {
  return {
    apiBaseUrl: 'http://localhost:5000/api',
    get: vi.fn(async () => undefined),
    getTextStream: vi.fn(async () => ({ stream, headers: new Headers() }))
  };
}

describe('Station SSE adapter', () => {
  it('uses the replay cursor and decodes initial, sequenced and transport heartbeat frames', async () => {
    const initial = JSON.stringify({
      eventSequenceId: 12,
      summary: stationSummary(),
      stations: [stationStatus()],
      recentResults: []
    });
    const station = JSON.stringify(stationStatus({ runtimeState: 'Idle' }));
    const api = apiFor(streamOf(
      `event: initialState\ndata: ${initial}\n\nid: 13\nevent: stationUpserted\n`,
      `data: ${station}\n\n:keepalive\n\n`
    ));
    const events: StationSseEvent[] = [];
    const onOpen = vi.fn();

    await createStationSseAdapter(api).connect({
      afterSequence: 9,
      signal: new AbortController().signal,
      onOpen,
      onEvent: event => events.push(event)
    });

    expect(api.getTextStream).toHaveBeenCalledWith('stations/events?afterSequence=9', expect.any(Object));
    expect(onOpen).toHaveBeenCalledOnce();
    expect(events).toEqual([
      { type: 'initialState', id: null, stationId: null, eventSequenceId: 12 },
      { type: 'stationUpserted', id: 13, stationId: 'station-a', eventSequenceId: null },
      { type: 'heartbeat', id: null, stationId: null, eventSequenceId: null }
    ]);
  });

  it('rejects malformed initial snapshots instead of inventing a cursor', async () => {
    const api = apiFor(streamOf('event: initialState\ndata: {"eventSequenceId":4}\n\n'));

    await expect(createStationSseAdapter(api).connect({
      afterSequence: 0,
      signal: new AbortController().signal,
      onOpen: vi.fn(),
      onEvent: vi.fn()
    })).rejects.toBeInstanceOf(StationContractDecodeError);
  });

  it('requires a positive id for stored events', async () => {
    const api = apiFor(streamOf(`event: stationUpserted\ndata: ${JSON.stringify(stationStatus())}\n\n`));

    await expect(createStationSseAdapter(api).connect({
      afterSequence: 0,
      signal: new AbortController().signal,
      onOpen: vi.fn(),
      onEvent: vi.fn()
    })).rejects.toThrow('positive sequence');
  });
});
