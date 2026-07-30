import { describe, expect, it, vi } from 'vitest';
import { ApiNetworkError, type ApiTransport } from '@/platform/api';
import { createAiSessionOwner } from '@/capabilities/ai-workbench/aiSessionOwner';
import {
  aiBuildRunId,
  aiOperationId,
  buildResultFixture,
  buildSessionFixture,
  validationFixture
} from './aiFixtures';
import { handoffArtifactPayload } from '../project-workspace/handoff/handoffFixtures';

function readyBuild() {
  return buildResultFixture({
    validation: validationFixture(true),
    buildIdentity: [
      'plan_production_identity',
      `sha256:${'a'.repeat(64)}`,
      `sha256:${'b'.repeat(64)}`,
      `sha256:${'c'.repeat(64)}`
    ].join(':')
  });
}

function readySession(build: ReturnType<typeof readyBuild>) {
  return buildSessionFixture(build, {
    planRunId: 'run_plan_01',
    planRunStatus: 'completed',
    planTerminalSequence: 3
  });
}

describe('F06 G4 AI handoff owner', () => {
  it('creates one artifact from the terminal eligible Build identity', async () => {
    const build = readyBuild();
    const session = readySession(build);
    const post = vi.fn(async (path: string, _body?: Record<string, unknown>) => {
      void _body;
      if (path === 'ai/handoffs') {
        return handoffArtifactPayload({
          build,
          buildIdentity: build.buildIdentity,
          sessionRevision: session.snapshot.revision
        });
      }
      throw new Error(`Unexpected POST ${path}`);
    });
    const api = {
      apiBaseUrl: 'http://localhost/api',
      get: vi.fn(async (path: string) => {
        if (path === 'ai/sessions/session_01') return session;
        throw new Error(`Unexpected GET ${path}`);
      }),
      post
    } as unknown as ApiTransport;
    const owner = createAiSessionOwner({
      api,
      requestedSessionId: 'session_01',
      operationIdFactory: () => aiOperationId
    });

    await owner.start();
    expect(owner.state.value.phase).toBe('build-ready');
    const created = await owner.prepareHandoff();
    expect(created, owner.state.value.message).toMatchObject({
      artifactId: '0123456789abcdef0123456789abcdef',
      buildRunId: aiBuildRunId
    });
    expect(build.buildIdentity.length).toBeGreaterThan(128);

    expect(post).toHaveBeenCalledTimes(1);
    expect(post.mock.calls[0]?.[1]).toMatchObject({
      clientOperationId: aiOperationId,
      buildRunId: aiBuildRunId,
      candidateFlowFingerprint: build.candidateFlowFingerprint,
      projectBaseline: build.projectBaseline
    });
    expect(post.mock.calls[0]?.[1]).not.toHaveProperty('candidateFlow');
    expect(owner.state.value.phase).toBe('handoff-created');
    owner.dispose();
  });

  it('reconciles a lost create response by Build lookup without repeating POST', async () => {
    const build = readyBuild();
    const session = readySession(build);
    const post = vi.fn(async (path: string, _body?: Record<string, unknown>) => {
      void _body;
      if (path === 'ai/handoffs') {
        throw new ApiNetworkError(path, new Error('response lost'));
      }
      throw new Error(`Unexpected POST ${path}`);
    });
    const api = {
      apiBaseUrl: 'http://localhost/api',
      get: vi.fn(async (path: string) => {
        if (path === 'ai/sessions/session_01') return session;
        if (path === `ai/handoffs/by-build/${aiBuildRunId}`) {
          return handoffArtifactPayload({
            build,
            buildIdentity: build.buildIdentity,
            sessionRevision: session.snapshot.revision
          });
        }
        throw new Error(`Unexpected GET ${path}`);
      }),
      post
    } as unknown as ApiTransport;
    const owner = createAiSessionOwner({
      api,
      requestedSessionId: 'session_01',
      operationIdFactory: () => aiOperationId
    });

    await owner.start();
    await expect(owner.prepareHandoff()).resolves.toBeNull();
    expect(owner.state.value.phase).toBe('handoff-unknown-outcome');
    const reconciled = await owner.reconcileHandoff();
    expect(reconciled, owner.state.value.message).toMatchObject({ buildRunId: aiBuildRunId });

    expect(post).toHaveBeenCalledTimes(1);
    expect(api.get).toHaveBeenCalledWith(`ai/handoffs/by-build/${aiBuildRunId}`, expect.anything());
    expect(owner.state.value.phase).toBe('handoff-created');
    owner.dispose();
    expect(owner.diagnostics()).toEqual({
      requestCount: 0, streamCount: 0, timerCount: 0, subscriptionCount: 0, disposed: true
    });
  });
});
