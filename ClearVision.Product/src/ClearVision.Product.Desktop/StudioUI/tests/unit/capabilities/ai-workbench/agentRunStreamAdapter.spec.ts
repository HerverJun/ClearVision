import { afterEach, describe, expect, it, vi } from 'vitest';
import { createAgentRunStreamAdapter } from '@/capabilities/ai-workbench/agentRunStreamAdapter';
import { createAiWorkbenchApi } from '@/capabilities/ai-workbench/apiAdapter';
import { createAiResourceLedger } from '@/capabilities/ai-workbench/resourceLedger';
import type { ApiTransport } from '@/platform/api';
import { replayFixture, runEventFixture } from './aiFixtures';

afterEach(() => vi.useRealTimers());

describe('AgentRun stream adapter', () => {
  it('pauses on a sequence gap, replays the missing event, and reaches terminal state', async () => {
    vi.useFakeTimers();
    let replayCount = 0;
    const firstReplay = replayFixture([runEventFixture(1)]);
    firstReplay.summary.status = 'running';
    const terminalReplay = replayFixture([
      runEventFixture(1),
      runEventFixture(2, 'plan.completed'),
      runEventFixture(3, 'run.completed')
    ]);
    const streamBody = new ReadableStream<Uint8Array>({
      start(controller) {
        const payload = JSON.stringify(runEventFixture(3, 'plan.model.completed'));
        controller.enqueue(new TextEncoder().encode(`id: 3\nevent: plan.model.completed\ndata: ${payload}\n\n`));
        controller.close();
      }
    });
    const transport = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(async () => replayCount++ === 0 ? firstReplay : terminalReplay),
      getTextStream: vi.fn(async () => ({ stream: streamBody, headers: new Headers() }))
    } as unknown as ApiTransport;
    const ledger = createAiResourceLedger();
    let lastSequence = 0;
    let terminal = false;
    let recovering = 0;
    const adapter = createAgentRunStreamAdapter({
      api: createAiWorkbenchApi(transport),
      ledger,
      getAfterSequence: () => lastSequence,
      isTerminal: () => terminal,
      onEvent: event => {
        if (event.sequence <= lastSequence) return 'stale';
        if (event.sequence > lastSequence + 1) return 'gap';
        lastSequence = event.sequence;
        terminal = event.eventType === 'run.completed';
        return 'accepted';
      },
      onRecovering: () => { recovering += 1; },
      onFailure: error => { throw error; }
    });

    await adapter.start('run_plan_01', 1);
    expect(lastSequence).toBe(1);
    expect(recovering).toBeGreaterThan(0);
    await vi.advanceTimersByTimeAsync(250);
    await Promise.resolve();

    expect(lastSequence).toBe(3);
    expect(terminal).toBe(true);
    adapter.dispose();
    ledger.dispose();
    expect(ledger.diagnostics()).toMatchObject({ requestCount: 0, streamCount: 0, timerCount: 0, disposed: true });
  });
});
