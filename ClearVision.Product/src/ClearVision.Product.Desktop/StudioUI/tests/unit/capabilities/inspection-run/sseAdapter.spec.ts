import { describe, expect, it, vi } from 'vitest';
import { createInspectionSseAdapter } from '@/capabilities/inspection-run';
import type { ApiTransport } from '@/platform/api';

describe('inspection SSE adapter', () => {
  it('decodes normal state and result events through the shared stream transport', async () => {
    const frames = [
      'id: 4\nevent: stateChanged\ndata: {"projectId":"p","sessionId":"s","oldState":"Starting","newState":"Running","errorMessage":null,"timestamp":"2026-07-26T00:00:00Z","isSnapshot":false,"startedAt":null,"stoppedAt":null}\n\n',
      'id: 5\nevent: resultProduced\ndata: {"projectId":"p","sessionId":"s","resultId":"r","status":"OK","executionOutcome":"Succeeded","decisionOutcome":"OK","defectCount":0,"processingTimeMs":3,"errorMessage":null,"timestamp":"2026-07-26T00:00:01Z"}\n\n'
    ];
    const stream = new ReadableStream<Uint8Array>({
      start(controller) { for (const frame of frames) controller.enqueue(new TextEncoder().encode(frame)); controller.close(); }
    });
    const api = { apiBaseUrl: 'http://localhost/api', get: vi.fn(),
      getTextStream: vi.fn(async () => ({ stream, headers: new Headers() })) } as ApiTransport;
    const events: string[] = [];

    await createInspectionSseAdapter(api).connect({ projectId: 'p', lastEventId: '3', signal: new AbortController().signal,
      onOpen: vi.fn(), onEvent: event => events.push(`${event.type}:${event.id}`) });

    expect(api.getTextStream).toHaveBeenCalledWith('inspection/realtime/p/events?lastEventId=3', expect.anything());
    expect(events).toEqual(['stateChanged:4', 'resultProduced:5']);
  });
});
