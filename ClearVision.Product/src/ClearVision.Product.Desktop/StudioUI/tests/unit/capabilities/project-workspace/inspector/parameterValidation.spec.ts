import { describe, expect, it } from 'vitest';
import {
  decodeInspectorParameterConstraints,
  isInspectorParameterMissing,
  validateInspectorParameterPatch,
  type InspectorParameterValidationDescriptor
} from '@/capabilities/project-workspace/inspector';

function parameter(overrides: Partial<InspectorParameterValidationDescriptor> = {}): InspectorParameterValidationDescriptor {
  return Object.freeze({
    name: 'Value',
    label: '值',
    dataType: 'string',
    isRequired: false,
    nullable: false,
    integer: false,
    options: null,
    minValue: null,
    maxValue: null,
    explicitValuePresent: true,
    value: '',
    defaultValue: null,
    ...overrides
  });
}

describe('G3 parameter validation', () => {
  it('keeps 0, false and "0" configured while null/undefined/blank/pending are missing', () => {
    expect([0, false, '0'].map(isInspectorParameterMissing)).toEqual([false, false, false]);
    expect([null, undefined, '', '  ', '<pending>', '<pending-file>'].map(isInspectorParameterMissing))
      .toEqual([true, true, true, true, true, true]);
  });

  it('validates string, boolean, integer, finite number, inclusive range and enum membership', () => {
    expect(validateInspectorParameterPatch([parameter()], [], 'Value', '')).toEqual([]);
    expect(validateInspectorParameterPatch([parameter({ dataType: 'bool' })], [], 'Value', false)).toEqual([]);
    expect(validateInspectorParameterPatch([
      parameter({ dataType: 'int', integer: true, minValue: 0, maxValue: 5 })
    ], [], 'Value', 0)).toEqual([]);
    expect(validateInspectorParameterPatch([
      parameter({ dataType: 'int', integer: true, minValue: 0, maxValue: 5 })
    ], [], 'Value', 1.2)[0]?.code).toBe('type');
    expect(validateInspectorParameterPatch([
      parameter({ dataType: 'double', minValue: 0, maxValue: 5 })
    ], [], 'Value', 6)[0]?.code).toBe('range');
    expect(validateInspectorParameterPatch([
      parameter({ options: [{ label: '自动', value: 'Auto' }] })
    ], [], 'Value', 'Manual')[0]?.code).toBe('enum');
  });

  it('distinguishes nullable from optional and never treats false/0 as required failures', () => {
    expect(validateInspectorParameterPatch([
      parameter({ dataType: 'int', integer: true, nullable: true })
    ], [], 'Value', null)).toEqual([]);
    expect(validateInspectorParameterPatch([
      parameter({ dataType: 'int', integer: true, nullable: false })
    ], [], 'Value', null)[0]?.code).toBe('type');
    expect(validateInspectorParameterPatch([
      parameter({ dataType: 'int', integer: true, nullable: false })
    ], [], 'Value', '')[0]?.code).toBe('type');
    expect(validateInspectorParameterPatch([
      parameter({ dataType: 'bool', isRequired: true })
    ], [], 'Value', false)).toEqual([]);
    expect(validateInspectorParameterPatch([
      parameter({ dataType: 'int', integer: true, isRequired: true })
    ], [], 'Value', 0)).toEqual([]);
  });

  it('applies requiredWhen, mutually-exclusive and satisfiedByInputPorts semantics', () => {
    const parameters = [
      parameter({ name: 'Mode', value: 'Manual' }),
      parameter({ name: 'Path', label: '路径', value: '', isRequired: false }),
      parameter({ name: 'ResourceId', value: '', isRequired: false })
    ];
    const constraints = decodeInspectorParameterConstraints([
      {
        parameter: 'Path', requiredPolicy: 'metadata',
        requiredWhen: { all: [{ parameter: 'Mode', comparison: 'equals', value: 'Manual' }] },
        mutuallyExclusiveGroup: 'resource', reasonCode: 'PATH_REQUIRED'
      },
      {
        parameter: 'ResourceId', requiredPolicy: 'required', mutuallyExclusiveGroup: 'resource',
        satisfiedByInputPorts: ['Resource'], reasonCode: 'RESOURCE_REQUIRED'
      }
    ]);

    expect(validateInspectorParameterPatch(parameters, constraints, 'Path', '')[0]?.code).toBe('required');
    expect(validateInspectorParameterPatch(parameters, constraints, 'Path', 'a.png')[0]?.code).toBeUndefined();
    expect(validateInspectorParameterPatch(
      parameters,
      constraints,
      'ResourceId',
      '',
      new Set(['Resource'])
    )).toEqual([]);
    const conflicting = parameters.map(item => item.name === 'Path'
      ? Object.freeze({ ...item, value: 'a.png' })
      : item);
    expect(validateInspectorParameterPatch(conflicting, constraints, 'ResourceId', 'asset-1')[0]?.code)
      .toBe('mutually-exclusive');
  });
});
