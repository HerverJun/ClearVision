import { describe, expect, it } from 'vitest';
import {
  decodeWorkspaceRunAdmissionV1,
  WorkspaceRunContractDecodeError
} from '@/capabilities/project-workspace/run';

describe('Workspace Run admission contract', () => {
  it('decodes actionable violations without inventing a second validation model', () => {
    const admission = decodeWorkspaceRunAdmissionV1({
      allowed: false,
      code: 'ADMISSION_FLOW_REQUIRED_PARAMETER_MISSING',
      message: 'Required parameter is missing.',
      projectId: '11111111-1111-4111-8111-111111111111',
      clientSnapshotId: '22222222-2222-4222-8222-222222222222',
      projectPersistenceRevision: null,
      canonicalFlowHash: null,
      decisionConfigurationHash: null,
      violations: [{
        operatorId: '33333333-3333-4333-8333-333333333333',
        operatorName: '测量',
        operatorType: 'Caliper',
        reason: 'Required parameter Width is missing.',
        parameterName: 'Width',
        code: 'FLOW_REQUIRED_PARAMETER_MISSING'
      }]
    });

    expect(admission.violations).toEqual([{
      operatorId: '33333333-3333-4333-8333-333333333333',
      operatorName: '测量',
      operatorType: 'Caliper',
      reason: 'Required parameter Width is missing.',
      parameterName: 'Width',
      code: 'FLOW_REQUIRED_PARAMETER_MISSING'
    }]);
  });

  it('fails closed when a violation has no authoritative reason', () => {
    expect(() => decodeWorkspaceRunAdmissionV1({
      allowed: false,
      code: 'ADMISSION_FLOW_INVALID',
      message: 'invalid',
      projectId: '11111111-1111-4111-8111-111111111111',
      clientSnapshotId: '22222222-2222-4222-8222-222222222222',
      projectPersistenceRevision: null,
      canonicalFlowHash: null,
      decisionConfigurationHash: null,
      violations: [{ code: 'FLOW_INVALID' }]
    })).toThrow(WorkspaceRunContractDecodeError);
  });
});
