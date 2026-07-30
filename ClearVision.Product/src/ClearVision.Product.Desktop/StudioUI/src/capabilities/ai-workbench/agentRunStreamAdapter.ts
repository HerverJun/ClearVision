import { ApiAbortError } from '@/platform/api';
import type { AiAgentRunEventV1, AiAgentRunReplayV1 } from './contracts';
import { decodeAiAgentRunEventV1 } from './decoder';
import type { AiWorkbenchApi } from './apiAdapter';
import type { AiResourceLedger } from './resourceLedger';

export type AiStreamEventOutcome = 'accepted' | 'gap' | 'stale';

export interface CreateAgentRunStreamAdapterOptions {
  readonly api: AiWorkbenchApi;
  readonly ledger: AiResourceLedger;
  readonly getAfterSequence: () => number;
  readonly isTerminal: () => boolean;
  readonly onEvent: (event: AiAgentRunEventV1, generation: number) => AiStreamEventOutcome;
  readonly onReplay: (replay: AiAgentRunReplayV1, generation: number) => void;
  readonly onRecovering: (message: string) => void;
  readonly onFailure: (error: unknown) => void;
}

export interface AgentRunStreamAdapter {
  start(runId: string, generation: number): Promise<void>;
  reconcile(): Promise<void>;
  dispose(): void;
}

export function createAgentRunStreamAdapter(options: CreateAgentRunStreamAdapterOptions): AgentRunStreamAdapter {
  let runId: string | null = null;
  let generation = 0;
  let connectionGeneration = 0;
  let disposed = false;
  let reconnectAttempt = 0;
  let reconnectRelease: (() => void) | null = null;
  let reconciling: Promise<void> | null = null;

  async function request<T>(run: (signal: AbortSignal) => Promise<T>): Promise<T> {
    const controller = new AbortController();
    const release = options.ledger.trackRequest(controller);
    try {
      return await run(controller.signal);
    } finally {
      release();
    }
  }

  function consume(event: AiAgentRunEventV1, expectedConnection: number): AiStreamEventOutcome {
    if (disposed || expectedConnection !== connectionGeneration || generation <= 0) return 'stale';
    if (event.runId !== runId) return 'stale';
    return options.onEvent(event, generation);
  }

  function cancelReconnect(): void {
    reconnectRelease?.();
    reconnectRelease = null;
  }

  function scheduleReconnect(): void {
    if (disposed || options.isTerminal() || reconnectRelease) return;
    reconnectAttempt += 1;
    const delay = Math.min(3000, 250 * (2 ** Math.min(reconnectAttempt - 1, 4)));
    options.onRecovering('事件流已中断，正在回放服务端事件并重新连接。');
    const timer = globalThis.setTimeout(() => {
      reconnectRelease?.();
      reconnectRelease = null;
      void reconcile();
    }, delay);
    reconnectRelease = options.ledger.trackTimer(timer);
  }

  async function consumeSse(
    stream: ReadableStream<Uint8Array>,
    controller: AbortController,
    expectedConnection: number
  ): Promise<void> {
    const reader = stream.getReader();
    const releaseStream = options.ledger.trackStream(() => {
      controller.abort('ai-plan-stream-disposed');
      void reader.cancel('ai-plan-stream-disposed');
    });
    const decoder = new TextDecoder();
    let buffer = '';
    try {
      while (!disposed && expectedConnection === connectionGeneration) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n');
        let boundary = buffer.indexOf('\n\n');
        while (boundary >= 0) {
          const block = buffer.slice(0, boundary);
          buffer = buffer.slice(boundary + 2);
          const data = block.split('\n')
            .filter(line => line.startsWith('data:'))
            .map(line => line.slice(5).trimStart())
            .join('\n');
          if (data) {
            const outcome = consume(decodeAiAgentRunEventV1(JSON.parse(data)), expectedConnection);
            if (outcome === 'gap') {
              options.onRecovering('检测到进度序号缺口，正在回放补齐。');
              controller.abort('ai-plan-sequence-gap');
              scheduleReconnect();
              return;
            }
          }
          boundary = buffer.indexOf('\n\n');
        }
        if (options.isTerminal()) return;
      }
      if (!disposed && expectedConnection === connectionGeneration && !options.isTerminal()) scheduleReconnect();
    } catch (error) {
      if (disposed || expectedConnection !== connectionGeneration || error instanceof ApiAbortError || controller.signal.aborted) {
        return;
      }
      scheduleReconnect();
    } finally {
      releaseStream();
      reader.releaseLock();
    }
  }

  async function openStream(expectedConnection: number): Promise<void> {
    if (disposed || !runId || options.isTerminal() || expectedConnection !== connectionGeneration) return;
    const controller = new AbortController();
    const releasePendingStream = options.ledger.trackStream(() => controller.abort('ai-plan-stream-disposed'));
    try {
      const response = await options.api.openRunEvents(runId, options.getAfterSequence(), controller.signal);
      releasePendingStream();
      if (disposed || expectedConnection !== connectionGeneration) {
        controller.abort('ai-plan-stream-stale');
        return;
      }
      reconnectAttempt = 0;
      await consumeSse(response.stream, controller, expectedConnection);
    } catch (error) {
      if (disposed || expectedConnection !== connectionGeneration || error instanceof ApiAbortError || controller.signal.aborted) {
        return;
      }
      scheduleReconnect();
    } finally {
      releasePendingStream();
    }
  }

  async function reconcile(): Promise<void> {
    if (reconciling) return reconciling;
    if (disposed || !runId) return;
    const expectedConnection = ++connectionGeneration;
    cancelReconnect();
    reconciling = (async () => {
      try {
        const replay = await request(signal => options.api.getRunReplay(runId!, signal));
        if (disposed || expectedConnection !== connectionGeneration) return;
        options.onReplay(replay, generation);
        for (const event of replay.events) {
          const outcome = consume(event, expectedConnection);
          if (outcome === 'gap') {
            throw new Error('AgentRun replay did not close the sequence gap.');
          }
        }
        if (!options.isTerminal() && replay.summary.status === 'running') {
          await openStream(expectedConnection);
        }
      } catch (error) {
        if (disposed || expectedConnection !== connectionGeneration || error instanceof ApiAbortError) return;
        options.onFailure(error);
      } finally {
        reconciling = null;
      }
    })();
    return reconciling;
  }

  return Object.freeze({
    async start(nextRunId: string, nextGeneration: number) {
      runId = nextRunId;
      generation = nextGeneration;
      reconnectAttempt = 0;
      await reconcile();
    },
    reconcile,
    dispose() {
      if (disposed) return;
      disposed = true;
      connectionGeneration += 1;
      cancelReconnect();
    }
  });
}
