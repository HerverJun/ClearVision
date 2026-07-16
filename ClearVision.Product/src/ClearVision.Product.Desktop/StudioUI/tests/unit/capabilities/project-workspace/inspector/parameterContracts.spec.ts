import { describe, expect, it } from 'vitest';
import {
  InspectorMetadataDecodeError,
  decodeInspectorOutputAvailabilityRules,
  decodeInspectorParameterConstraints
} from '@/capabilities/project-workspace/inspector';

describe('G3 Inspector metadata contracts', () => {
  it('decodes the stable constraint and output rule shapes', () => {
    const constraints = decodeInspectorParameterConstraints([{
      Parameter: 'Threshold',
      RequiredPolicy: 'required',
      RequiredWhen: { All: [{ Parameter: 'Enabled', Comparison: 'equals', Value: true }] },
      EnabledWhen: null,
      DisabledWhen: null,
      VisibleWhen: null,
      HiddenWhen: null,
      IgnoredWhen: null,
      AtLeastOneGroup: 'threshold-source',
      MutuallyExclusiveGroup: null,
      AliasFor: null,
      Deprecated: false,
      ResourceKind: null,
      ReasonCode: 'THRESHOLD_REQUIRED',
      SatisfiedByInputPorts: ['ThresholdInput']
    }]);
    const outputs = decodeInspectorOutputAvailabilityRules([{
      output: 'Binary',
      availableWhen: { any: [{ parameter: 'Enabled', comparison: 'equals', value: true }] },
      reasonCode: 'BINARY_DISABLED'
    }]);

    expect(constraints[0]).toMatchObject({
      parameter: 'Threshold',
      requiredPolicy: 'required',
      atLeastOneGroup: 'threshold-source',
      satisfiedByInputPorts: ['ThresholdInput']
    });
    expect(constraints[0]?.requiredWhen?.all[0]).toEqual({
      parameter: 'Enabled', comparison: 'equals', value: true
    });
    expect(outputs[0]).toMatchObject({ output: 'Binary', reasonCode: 'BINARY_DISABLED' });
  });

  it('rejects unsupported comparison syntax as metadata decode failure', () => {
    expect(() => decodeInspectorParameterConstraints([{
      parameter: 'Threshold',
      requiredPolicy: 'metadata',
      requiredWhen: { all: [{ parameter: 'Mode', comparison: 'contains', value: 'x' }] }
    }])).toThrow(InspectorMetadataDecodeError);
  });
});
