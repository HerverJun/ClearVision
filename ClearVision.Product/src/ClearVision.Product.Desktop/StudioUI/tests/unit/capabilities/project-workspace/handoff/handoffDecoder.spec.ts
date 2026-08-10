import { describe, expect, it } from 'vitest';
import {
  WorkspaceHandoffContractError,
  decodeWorkspaceHandoffArtifactV1
} from '@/capabilities/project-workspace';
import { handoffArtifactPayload } from './handoffFixtures';

describe('F06 G4 Workspace handoff decoder', () => {
  it('decodes a redacted eligible artifact without retaining a private owner identity', () => {
    const artifact = decodeWorkspaceHandoffArtifactV1(handoffArtifactPayload());

    expect(artifact).toMatchObject({
      targetKind: 'new',
      status: 'available',
      projectBaseline: { projectId: null, persistenceRevision: null }
    });
    expect(artifact).not.toHaveProperty('ownerHash');
  });

  it('accepts the bounded production Build identity used by the canonical backend', () => {
    const payload = handoffArtifactPayload();
    const buildIdentity = [
      'plan_production_identity',
      `sha256:${'a'.repeat(64)}`,
      `sha256:${'b'.repeat(64)}`,
      `sha256:${'c'.repeat(64)}`
    ].join(':');
    const artifact = decodeWorkspaceHandoffArtifactV1({
      ...payload,
      buildIdentity,
      build: {
        ...(payload.build as Record<string, unknown>),
        buildIdentity
      }
    });

    expect(buildIdentity.length).toBeGreaterThan(128);
    expect(artifact.buildIdentity).toBe(buildIdentity);
  });

  it('rejects unknown public fields and a receipt that claims Project persistence', () => {
    expect(() => decodeWorkspaceHandoffArtifactV1({
      ...handoffArtifactPayload(),
      ownerHash: 'private-owner'
    })).toThrow(WorkspaceHandoffContractError);

    let receiptFailure: unknown;
    try {
      decodeWorkspaceHandoffArtifactV1({
        ...handoffArtifactPayload({ status: 'consumed' }),
        consumeReceipt: {
          clientOperationId: '55555555-5555-4555-8555-555555555555',
          acknowledgedAtUtc: '2026-07-29T08:05:00.000Z',
          projectSaved: true
        }
      });
    } catch (error) {
      receiptFailure = error;
    }
    expect(receiptFailure).toMatchObject({ path: '$.consumeReceipt.projectSaved' });
  });

  it('rejects a new target carrying a Project identity and a mismatched candidate fingerprint', () => {
    let baselineFailure: unknown;
    try {
      decodeWorkspaceHandoffArtifactV1(handoffArtifactPayload({
        projectBaseline: {
          targetKind: 'new', projectId: '22222222-2222-4222-8222-222222222222',
          persistenceRevision: 0, canonicalFlowHash: ''
        }
      }));
    } catch (error) {
      baselineFailure = error;
    }
    expect(baselineFailure).toMatchObject({ path: '$.projectBaseline' });

    let fingerprintFailure: unknown;
    try {
      decodeWorkspaceHandoffArtifactV1(handoffArtifactPayload({
        candidateFlowFingerprint: 'f'.repeat(64)
      }));
    } catch (error) {
      fingerprintFailure = error;
    }
    expect(fingerprintFailure).toMatchObject({ path: '$.build' });
  });
});
