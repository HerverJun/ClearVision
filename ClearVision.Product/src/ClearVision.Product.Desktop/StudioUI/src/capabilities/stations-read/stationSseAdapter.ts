import type { ApiTransport } from '@/platform/api';
import {
  StationContractDecodeError,
  decodeHealthSnapshot,
  decodeStationList,
  decodeStationLogs,
  decodeStationResult,
  decodeStationStatus,
  decodeStationSummary
} from './stationContracts';

export interface StationSseEvent {
  readonly type: string;
  readonly id: number | null;
  readonly stationId: string | null;
  readonly eventSequenceId: number | null;
}

export interface StationSseConnectionOptions {
  readonly afterSequence: number;
  readonly signal: AbortSignal;
  readonly onOpen: () => void;
  readonly onEvent: (event: StationSseEvent) => void;
}

export interface StationSsePort {
  connect(options: StationSseConnectionOptions): Promise<void>;
}

export interface StationSseFrame {
  readonly type: string;
  readonly id: string | null;
  readonly payload: unknown;
}

function record(value: unknown, path: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new StationContractDecodeError(path, 'an object');
  }
  return value as Record<string, unknown>;
}

function stringField(value: unknown, path: string): string {
  if (typeof value !== 'string' || !value.trim()) {
    throw new StationContractDecodeError(path, 'a non-empty string');
  }
  return value;
}

function sequenceField(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) {
    throw new StationContractDecodeError(path, 'a non-negative safe integer');
  }
  return value;
}

function decodeId(value: string | null): number | null {
  if (value === null || value === '') return null;
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed < 1) {
    throw new StationContractDecodeError('event.id', 'a positive safe integer');
  }
  return parsed;
}

function decodeEnvelopeStationId(payload: unknown, path = '$'): string {
  const envelope = record(payload, path);
  return stringField(envelope.stationId, `${path}.stationId`);
}

export function decodeStationSseEvent(frame: StationSseFrame): StationSseEvent {
  const id = decodeId(frame.id);
  let stationId: string | null = null;
  let eventSequenceId: number | null = null;
  const sequencedEvent = frame.type !== 'heartbeat' && frame.type !== 'initialState';
  if (sequencedEvent && id === null) {
    throw new StationContractDecodeError('event.id', 'a positive sequence for a stored event');
  }

  switch (frame.type) {
    case 'heartbeat':
      break;
    case 'initialState': {
      const snapshot = record(frame.payload, '$');
      eventSequenceId = sequenceField(snapshot.eventSequenceId, '$.eventSequenceId');
      decodeStationSummary(snapshot.summary);
      decodeStationList(snapshot.stations);
      if (!Array.isArray(snapshot.recentResults)) {
        throw new StationContractDecodeError('$.recentResults', 'an array');
      }
      snapshot.recentResults.forEach((value, index) => {
        const path = `$.recentResults[${index}]`;
        const envelope = record(value, path);
        stringField(envelope.stationId, `${path}.stationId`);
        decodeStationResult(envelope.result, `${path}.result`);
        decodeStationStatus(envelope.station, `${path}.station`);
      });
      break;
    }
    case 'stationUpserted':
      stationId = decodeStationStatus(frame.payload).stationId;
      break;
    case 'summaryUpdated':
      decodeStationSummary(frame.payload);
      break;
    case 'stationResultAdded': {
      const envelope = record(frame.payload, '$');
      stationId = decodeEnvelopeStationId(envelope);
      decodeStationResult(envelope.result, '$.result');
      decodeStationStatus(envelope.station, '$.station');
      break;
    }
    case 'stationHealthUpdated': {
      const envelope = record(frame.payload, '$');
      stationId = decodeEnvelopeStationId(envelope);
      decodeHealthSnapshot(envelope.health, '$.health');
      decodeStationStatus(envelope.station, '$.station');
      break;
    }
    case 'stationLogAdded': {
      const envelope = record(frame.payload, '$');
      stationId = decodeEnvelopeStationId(envelope);
      decodeStationLogs([envelope.log]);
      decodeStationStatus(envelope.station, '$.station');
      break;
    }
    case 'stationCommandUpdated':
      stationId = decodeEnvelopeStationId(frame.payload);
      break;
    default:
      break;
  }

  return Object.freeze({ type: frame.type, id, stationId, eventSequenceId });
}

function parseFrame(frame: string): StationSseFrame | null {
  let type = 'message';
  let id: string | null = null;
  let keepalive = false;
  const data: string[] = [];

  for (const line of frame.split(/\r\n|\r|\n/)) {
    if (!line) continue;
    if (line.startsWith(':')) {
      keepalive ||= line.slice(1).trim().toLocaleLowerCase() === 'keepalive';
      continue;
    }
    const separator = line.indexOf(':');
    const field = separator < 0 ? line : line.slice(0, separator);
    const raw = separator < 0 ? '' : line.slice(separator + 1);
    const value = raw.startsWith(' ') ? raw.slice(1) : raw;
    if (field === 'event') type = value;
    else if (field === 'id') id = value;
    else if (field === 'data') data.push(value);
  }

  if (data.length === 0) {
    return keepalive ? Object.freeze({ type: 'heartbeat', id: null, payload: null }) : null;
  }
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

export function createStationSseAdapter(api: ApiTransport): StationSsePort {
  if (!api.getTextStream) throw new Error('Station SSE requires the shared streaming transport.');
  const getTextStream = api.getTextStream.bind(api);

  return Object.freeze({
    async connect(options: StationSseConnectionOptions): Promise<void> {
      const cursor = options.afterSequence > 0
        ? `?afterSequence=${encodeURIComponent(String(options.afterSequence))}`
        : '';
      const response = await getTextStream(`stations/events${cursor}`, { signal: options.signal });
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
            const parsed = parseFrame(buffer.slice(0, boundary.index));
            buffer = buffer.slice(boundary.index + boundary.length);
            if (parsed) options.onEvent(decodeStationSseEvent(parsed));
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
