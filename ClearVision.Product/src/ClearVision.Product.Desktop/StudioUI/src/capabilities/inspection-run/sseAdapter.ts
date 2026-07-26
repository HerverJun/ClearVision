import type { ApiTransport } from '@/platform/api';
import { decodeInspectionSseEvent, type InspectionSseEvent } from './contracts';

export interface InspectionSseConnectionOptions {
  readonly projectId: string;
  readonly lastEventId: string | null;
  readonly signal: AbortSignal;
  readonly onOpen: () => void;
  readonly onEvent: (event: InspectionSseEvent) => void;
}

export interface InspectionSsePort {
  connect(options: InspectionSseConnectionOptions): Promise<void>;
}

function parseFrame(frame: string): Readonly<{ type: string; id: string | null; payload: unknown }> | null {
  let type = 'message';
  let id: string | null = null;
  const data: string[] = [];
  for (const line of frame.split(/\r\n|\r|\n/)) {
    if (!line || line.startsWith(':')) continue;
    const separator = line.indexOf(':');
    const field = separator < 0 ? line : line.slice(0, separator);
    const raw = separator < 0 ? '' : line.slice(separator + 1);
    const value = raw.startsWith(' ') ? raw.slice(1) : raw;
    if (field === 'event') type = value;
    else if (field === 'id') id = value;
    else if (field === 'data') data.push(value);
  }
  if (data.length === 0) return null;
  return Object.freeze({ type, id, payload: JSON.parse(data.join('\n')) as unknown });
}

function findFrameBoundary(buffer: string): Readonly<{ index: number; length: number }> | null {
  const candidates = [
    { index: buffer.indexOf('\r\n\r\n'), length: 4 },
    { index: buffer.indexOf('\n\n'), length: 2 },
    { index: buffer.indexOf('\r\r'), length: 2 }
  ].filter(candidate => candidate.index >= 0);
  if (candidates.length === 0) return null;
  return candidates.reduce((earliest, candidate) => candidate.index < earliest.index ? candidate : earliest);
}

export function createInspectionSseAdapter(api: ApiTransport): InspectionSsePort {
  if (!api.getTextStream) throw new Error('Inspection SSE requires the shared streaming transport.');
  const getTextStream = api.getTextStream.bind(api);
  return Object.freeze({
    async connect(options: InspectionSseConnectionOptions) {
      const cursor = options.lastEventId ? `?lastEventId=${encodeURIComponent(options.lastEventId)}` : '';
      const response = await getTextStream(
        `inspection/realtime/${encodeURIComponent(options.projectId)}/events${cursor}`,
        { signal: options.signal }
      );
      options.onOpen();
      const reader = response.stream.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      try {
        while (!options.signal.aborted) {
          const chunk = await reader.read();
          if (chunk.done) break;
          buffer += decoder.decode(chunk.value, { stream: true });
          let boundary = findFrameBoundary(buffer);
          while (boundary) {
            const frame = parseFrame(buffer.slice(0, boundary.index));
            buffer = buffer.slice(boundary.index + boundary.length);
            if (frame) {
              const event = decodeInspectionSseEvent(frame.type, frame.id, frame.payload);
              if (event) options.onEvent(event);
            }
            boundary = findFrameBoundary(buffer);
          }
        }
      } finally {
        await reader.cancel().catch(() => {});
        reader.releaseLock();
      }
    }
  });
}
