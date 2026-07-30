import { describe, expect, it, vi } from 'vitest';
import type { ApiTransport } from '@/platform/api';
import {
  createWorkspaceHandoffReceivePort,
  decodeWorkspaceHandoffArtifactV1
} from '@/capabilities/project-workspace';
import {
  artifactId,
  consumeOperationId,
  existingProjectId,
  handoffArtifactPayload
} from './handoffFixtures';

function apiFor(initial: Record<string, unknown>) {
  const current = { value: initial };
  const get = vi.fn(async () => current.value);
  const post = vi.fn(async (path: string, body?: Record<string, unknown>) => {
    if (path.endsWith('/consume')) {
      current.value = handoffArtifactPayload({
        ...current.value,
        status: 'consuming',
        consumeClientOperationId: body?.clientOperationId
      });
      return current.value;
    }
    if (path.endsWith('/acknowledge')) {
      current.value = handoffArtifactPayload({
        ...current.value,
        status: 'consumed',
        consumeClientOperationId: body?.clientOperationId,
        consumeReceipt: {
          clientOperationId: body?.clientOperationId,
          acknowledgedAtUtc: '2026-07-29T08:05:00.000Z',
          projectSaved: false
        }
      });
      return current.value;
    }
    return current.value;
  });
  return {
    api: { apiBaseUrl: 'http://localhost/api', get, post } as unknown as ApiTransport,
    get,
    post,
    current
  };
}

describe('F06 G4 Workspace handoff receive port', () => {
  it('reserves, stages and acknowledges once with a source projection only', async () => {
    const transport = apiFor(handoffArtifactPayload());
    const stage = vi.fn(async artifact => {
      expect(artifact.status).toBe('consuming');
    });
    const port = createWorkspaceHandoffReceivePort({
      api: transport.api,
      operationIdFactory: () => consumeOperationId,
      now: () => new Date('2026-07-29T08:05:00.000Z')
    });

    const result = await port.receive({
      artifactId, targetProjectId: null, isDirty: () => false, baselineMatches: () => true, stage
    });

    expect(result?.source).toEqual({
      artifactId,
      sessionId: 'session_01',
      planId: 'plan_fixture_01',
      buildId: 'build_fixture_01',
      candidateFlowFingerprint: 'e'.repeat(64),
      targetKind: 'new',
      receivedAtUtc: '2026-07-29T08:05:00.000Z'
    });
    expect(transport.post.mock.calls.map(([path]) => path)).toEqual([
      `ai/handoffs/${artifactId}/consume`,
      `ai/handoffs/${artifactId}/acknowledge`
    ]);
    expect(port.projection.phase).toBe('workspace-staged-unsaved');
    port.dispose();
  });

  it('fails closed for dirty, expired, consumed and baseline-conflicting Workspaces', async () => {
    const cases = [
      { payload: handoffArtifactPayload(), dirty: true, projectId: null, phase: 'workspace-dirty-conflict' },
      { payload: handoffArtifactPayload({ status: 'expired' }), dirty: false, projectId: null, phase: 'artifact-expired' },
      { payload: handoffArtifactPayload({ status: 'consumed' }), dirty: false, projectId: null, phase: 'artifact-consumed' },
      {
        payload: handoffArtifactPayload({ targetKind: 'existing' }), dirty: false,
        projectId: '77777777-7777-4777-8777-777777777777', phase: 'artifact-baseline-conflict'
      }
    ] as const;

    for (const testCase of cases) {
      const transport = apiFor(testCase.payload);
      const port = createWorkspaceHandoffReceivePort({ api: transport.api });
      await expect(port.receive({
        artifactId,
        targetProjectId: testCase.projectId,
        isDirty: () => testCase.dirty,
        baselineMatches: () => true,
        stage: vi.fn()
      })).resolves.toBeNull();
      expect(port.projection.phase).toBe(testCase.phase);
      expect(transport.post).not.toHaveBeenCalled();
      port.dispose();
    }
  });

  it('leaves a reserved artifact consuming when staging fails and never fabricates consumed', async () => {
    const transport = apiFor(handoffArtifactPayload());
    const port = createWorkspaceHandoffReceivePort({
      api: transport.api,
      operationIdFactory: () => consumeOperationId
    });

    await expect(port.receive({
      artifactId,
      targetProjectId: null,
      isDirty: () => false,
      baselineMatches: () => true,
      stage: async () => { throw new Error('canvas mount failed'); }
    })).resolves.toBeNull();

    expect(decodeWorkspaceHandoffArtifactV1(transport.current.value).status).toBe('consuming');
    expect(transport.post.mock.calls.map(([path]) => path)).toEqual([
      `ai/handoffs/${artifactId}/consume`
    ]);
    expect(port.projection.phase).toBe('error');
    port.dispose();
  });

  it('reuses the server consume identity after response loss and completes the same reserve/ack', async () => {
    const transport = apiFor(handoffArtifactPayload({
      status: 'consuming',
      consumeClientOperationId: consumeOperationId
    }));
    const port = createWorkspaceHandoffReceivePort({
      api: transport.api,
      operationIdFactory: () => '88888888-8888-4888-8888-888888888888'
    });

    await expect(port.receive({
      artifactId,
      targetProjectId: null,
      isDirty: () => false,
      baselineMatches: () => true,
      stage: async () => undefined
    })).resolves.toBeTruthy();

    expect(transport.post.mock.calls[0]?.[1]).toMatchObject({ clientOperationId: consumeOperationId });
    expect(transport.post.mock.calls[1]?.[1]).toMatchObject({ clientOperationId: consumeOperationId });
    port.dispose();
  });

  it('accepts the exact existing Project baseline receiver identity', async () => {
    const transport = apiFor(handoffArtifactPayload({ targetKind: 'existing' }));
    const port = createWorkspaceHandoffReceivePort({
      api: transport.api,
      operationIdFactory: () => consumeOperationId
    });

    await expect(port.receive({
      artifactId,
      targetProjectId: existingProjectId,
      isDirty: () => false,
      baselineMatches: () => true,
      stage: async () => undefined
    })).resolves.toBeTruthy();
    expect(port.projection.phase).toBe('workspace-staged-unsaved');
    port.dispose();
  });

  it('rejects a changed existing Project revision before reserving the artifact', async () => {
    const transport = apiFor(handoffArtifactPayload({ targetKind: 'existing' }));
    const port = createWorkspaceHandoffReceivePort({ api: transport.api });

    await expect(port.receive({
      artifactId,
      targetProjectId: existingProjectId,
      isDirty: () => false,
      baselineMatches: () => false,
      stage: async () => undefined
    })).resolves.toBeNull();

    expect(port.projection.phase).toBe('artifact-baseline-conflict');
    expect(transport.post).not.toHaveBeenCalled();
    port.dispose();
  });
});
