import { describe, expect, it } from 'vitest';
import { validateBuildParameterValues } from '@/capabilities/ai-workbench/parameterValidation';
import { buildParameterFixture } from './aiFixtures';

describe('AI Build parameter contract validation', () => {
  it('distinguishes null and empty strings according to required policy', () => {
    const optional = buildParameterFixture({
      canonicalKey: 'threshold_1.label', parameterName: 'label', dataType: 'string',
      isRequired: false, requiredPolicy: 'optional', minValue: null, maxValue: null
    });
    expect(validateBuildParameterValues([optional] as never, { 'threshold_1.label': null }).valid).toBe(true);
    expect(validateBuildParameterValues([optional] as never, { 'threshold_1.label': '' }).valid).toBe(true);
    const required = { ...optional, isRequired: true, requiredPolicy: 'required' };
    expect(validateBuildParameterValues([required] as never, { 'threshold_1.label': null }).valid).toBe(false);
    expect(validateBuildParameterValues([required] as never, { 'threshold_1.label': '' }).valid).toBe(false);
  });

  it('validates integer type, numeric range and declared enum values', () => {
    const integer = buildParameterFixture({ dataType: 'int', minValue: 1, maxValue: 10 });
    expect(validateBuildParameterValues([integer] as never, { 'threshold_1.threshold': 2.5 }).valid).toBe(false);
    expect(validateBuildParameterValues([integer] as never, { 'threshold_1.threshold': 11 }).valid).toBe(false);
    expect(validateBuildParameterValues([integer] as never, { 'threshold_1.threshold': 5 }).valid).toBe(true);

    const selection = buildParameterFixture({
      canonicalKey: 'threshold_1.mode', parameterName: 'mode', dataType: 'string',
      options: [{ label: 'Fast', value: 'fast' }], minValue: null, maxValue: null
    });
    expect(validateBuildParameterValues([selection] as never, { 'threshold_1.mode': 'slow' }).valid).toBe(false);
    expect(validateBuildParameterValues([selection] as never, { 'threshold_1.mode': 'fast' }).valid).toBe(true);
  });

  it('enforces conditional required, enabled, at-least-one and mutually-exclusive contracts', () => {
    const mode = buildParameterFixture({
      canonicalKey: 'threshold_1.mode', parameterName: 'mode', dataType: 'string',
      isRequired: true, minValue: null, maxValue: null, pending: false
    });
    const conditional = buildParameterFixture({
      canonicalKey: 'threshold_1.advanced', parameterName: 'advanced', dataType: 'number',
      requiredWhen: {
        allConditions: [{ parameter: 'mode', comparison: 'equals', value: 'advanced' }],
        anyConditions: []
      }
    });
    expect(validateBuildParameterValues([mode, conditional] as never, {
      'threshold_1.mode': 'advanced'
    }).errors['threshold_1.advanced']).toBeTruthy();
    expect(validateBuildParameterValues([mode, conditional] as never, {
      'threshold_1.mode': 'basic'
    }).errors['threshold_1.advanced']).toBeUndefined();

    const groupA = buildParameterFixture({
      canonicalKey: 'threshold_1.source_a', parameterName: 'source_a', dataType: 'string',
      isRequired: false, requiredPolicy: 'optional', atLeastOneGroup: 'source', mutuallyExclusiveGroup: 'source',
      minValue: null, maxValue: null
    });
    const groupB = { ...groupA, canonicalKey: 'threshold_1.source_b', parameterName: 'source_b' };
    expect(validateBuildParameterValues([groupA, groupB] as never, {}).valid).toBe(false);
    expect(validateBuildParameterValues([groupA, groupB] as never, { 'threshold_1.source_a': 'a' }).valid).toBe(true);
    expect(validateBuildParameterValues([groupA, groupB] as never, {
      'threshold_1.source_a': 'a', 'threshold_1.source_b': 'b'
    }).valid).toBe(false);
  });

  it('preserves canonical all/any grouping including empty groups', () => {
    const mode = buildParameterFixture({
      canonicalKey: 'threshold_1.mode', parameterName: 'mode', dataType: 'string',
      isRequired: true, minValue: null, maxValue: null, pending: false
    });
    const profile = buildParameterFixture({
      canonicalKey: 'threshold_1.profile', parameterName: 'profile', dataType: 'string',
      isRequired: false, requiredPolicy: 'optional', minValue: null, maxValue: null, pending: false
    });
    const conditional = buildParameterFixture({
      canonicalKey: 'threshold_1.value', parameterName: 'value', dataType: 'string',
      requiredWhen: {
        allConditions: [{ parameter: 'mode', comparison: 'equals', value: 'advanced' }],
        anyConditions: [
          { parameter: 'profile', comparison: 'equals', value: 'line-a' },
          { parameter: 'profile', comparison: 'equals', value: 'line-b' }
        ]
      },
      minValue: null,
      maxValue: null
    });
    expect(validateBuildParameterValues([mode, profile, conditional] as never, {
      'threshold_1.mode': 'advanced', 'threshold_1.profile': 'line-b'
    }).errors['threshold_1.value']).toBeTruthy();
    expect(validateBuildParameterValues([mode, profile, conditional] as never, {
      'threshold_1.mode': 'advanced', 'threshold_1.profile': 'line-c'
    }).errors['threshold_1.value']).toBeUndefined();
    expect(validateBuildParameterValues([{ ...conditional, requiredWhen: {
      allConditions: [], anyConditions: []
    } }] as never, {}).errors['threshold_1.value']).toBeTruthy();
    expect(validateBuildParameterValues([{ ...conditional, requiredWhen: {
      allConditions: [{ parameter: 'value', comparison: 'empty', value: null }], anyConditions: []
    } }] as never, {}).errors['threshold_1.value']).toBeTruthy();
  });

  it('fails closed on unknown operator parameters, comparisons and data types', () => {
    const unknownParameter = buildParameterFixture({
      requiredWhen: {
        allConditions: [{ parameter: 'missing', comparison: 'equals', value: true }],
        anyConditions: []
      }
    });
    expect(validateBuildParameterValues([unknownParameter] as never, {}).valid).toBe(false);

    const unknownComparison = buildParameterFixture({
      requiredWhen: {
        allConditions: [{ parameter: 'threshold', comparison: 'contains', value: 'x' }],
        anyConditions: []
      }
    });
    expect(validateBuildParameterValues([unknownComparison] as never, {}).valid).toBe(false);
    expect(validateBuildParameterValues([
      buildParameterFixture({ dataType: 'opaque-contract-type' })
    ] as never, {}).valid).toBe(false);
    expect(validateBuildParameterValues([
      buildParameterFixture({ operatorType: '' })
    ] as never, {}).valid).toBe(false);
  });
});
