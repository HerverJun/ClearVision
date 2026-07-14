import test from 'node:test';
import assert from 'node:assert/strict';
import {
  collectEffectiveRequiredParameterErrors,
  getOperatorOutputAvailabilityStates,
  getOutputAvailabilityState,
  getOperatorParameterValue,
  getParameterEffectiveState,
  isEmptyParameterValue,
  isPendingParameterSentinel
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/shared/parameterDependencyRules.js';

function imageAcquisitionConstraints() {
  return [
    {
      parameter: 'CameraId',
      requiredPolicy: 'optional',
      requiredWhen: { all: [{ parameter: 'SourceType', comparison: 'equals', value: 'Camera' }] },
      disabledWhen: { all: [{ parameter: 'SourceType', comparison: 'equals', value: 'File' }] },
      atLeastOneGroup: 'image-camera-source'
    },
    {
      parameter: 'CameraBindingId',
      requiredPolicy: 'optional',
      aliasFor: 'CameraId',
      deprecated: true
    },
    {
      parameter: 'cameraId',
      requiredPolicy: 'optional',
      aliasFor: 'CameraId',
      deprecated: true
    }
  ];
}

function acquisition(parameters) {
  return {
    type: 'ImageAcquisition',
    parameterConstraints: imageAcquisitionConstraints(),
    parameters
  };
}

test('pending sentinel contract is exact and does not classify business todo values as missing', () => {
  for (const value of ['<pending>', '<pending-camera-binding>', ' <PENDING-model-resource> ']) {
    assert.equal(isPendingParameterSentinel(value), true, value);
    assert.equal(isEmptyParameterValue(value), true, value);
  }

  for (const value of ['<pending-camera binding>', '<pending-camera-binding', '<pendingish>', 'todo-line-camera', 'customer-todo-approved']) {
    assert.equal(isPendingParameterSentinel(value), false, value);
    assert.equal(isEmptyParameterValue(value), false, value);
  }
});

test('a present canonical parameter wins over aliases, including when it equals its metadata default', () => {
  const aliasOnly = acquisition([
    { name: 'SourceType', value: 'Camera', defaultValue: 'File' },
    { name: 'CameraBindingId', value: 'binding-camera' }
  ]);
  assert.equal(getOperatorParameterValue(aliasOnly, 'CameraId'), 'binding-camera');
  assert.equal(getOperatorParameterValue(aliasOnly, 'CameraBindingId'), 'binding-camera');

  const legacyAlias = acquisition([
    { name: 'SourceType', value: 'Camera', defaultValue: 'File' },
    { name: 'cameraId', value: 'legacy-camera' }
  ]);
  assert.equal(getOperatorParameterValue(legacyAlias, 'CameraId'), 'legacy-camera');

  const canonicalDefaultConflict = acquisition([
    { name: 'SourceType', value: 'Camera', defaultValue: 'File' },
    { name: 'CameraId', value: '', defaultValue: '' },
    { name: 'CameraBindingId', value: 'binding-camera' },
    { name: 'cameraId', value: 'legacy-camera' }
  ]);
  assert.equal(getOperatorParameterValue(canonicalDefaultConflict, 'CameraId'), '');
  assert.equal(getOperatorParameterValue(canonicalDefaultConflict, 'CameraBindingId'), '');
  assert.equal(getOperatorParameterValue(canonicalDefaultConflict, 'cameraId'), '');

  const conflict = acquisition([
    { name: 'SourceType', value: 'Camera', defaultValue: 'File' },
    { name: 'CameraId', value: 'canonical-camera', defaultValue: '' },
    { name: 'CameraBindingId', value: 'binding-camera' },
    { name: 'cameraId', value: 'legacy-camera' }
  ]);
  assert.equal(getOperatorParameterValue(conflict, 'CameraId'), 'canonical-camera');
  assert.equal(getOperatorParameterValue(conflict, 'CameraBindingId'), 'canonical-camera');
  assert.equal(getOperatorParameterValue(conflict, 'cameraId'), 'canonical-camera');
});

test('pending and business todo values produce the same property-panel readiness semantics as C#', () => {
  const pending = acquisition([
    { name: 'SourceType', value: 'Camera', defaultValue: 'File' },
    { name: 'CameraId', value: '<pending-camera-binding>', defaultValue: '' },
    { name: 'CameraBindingId', value: '' }
  ]);
  const pendingErrors = collectEffectiveRequiredParameterErrors(pending);
  assert.equal(pendingErrors.length, 1);
  assert.equal(pendingErrors[0].kind, 'required');

  const businessTodo = acquisition([
    { name: 'SourceType', value: 'Camera', defaultValue: 'File' },
    { name: 'CameraId', value: 'todo-line-camera', defaultValue: '' },
    { name: 'CameraBindingId', value: '' }
  ]);
  assert.deepEqual(collectEffectiveRequiredParameterErrors(businessTodo), []);
});

test('groups ignore stale values held by conditionally disabled parameters', () => {
  const constraints = [
    {
      parameter: 'A',
      requiredPolicy: 'optional',
      requiredWhen: { all: [{ parameter: 'Mode', comparison: 'equals', value: 'A' }] },
      disabledWhen: { all: [{ parameter: 'Mode', comparison: 'equals', value: 'B' }] },
      atLeastOneGroup: 'active-source',
      mutuallyExclusiveGroup: 'active-source'
    },
    {
      parameter: 'B',
      requiredPolicy: 'optional',
      requiredWhen: { all: [{ parameter: 'Mode', comparison: 'equals', value: 'B' }] },
      disabledWhen: { all: [{ parameter: 'Mode', comparison: 'equals', value: 'A' }] },
      atLeastOneGroup: 'active-source',
      mutuallyExclusiveGroup: 'active-source'
    }
  ];
  const operator = {
    type: 'GroupParity',
    parameterConstraints: constraints,
    parameters: [
      { name: 'Mode', value: 'A', defaultValue: 'A' },
      { name: 'A', value: '', defaultValue: '' },
      { name: 'B', value: 'stale-disabled-value', defaultValue: '' }
    ]
  };

  assert.equal(getParameterEffectiveState(operator, 'A').effectiveDisabled, false);
  assert.equal(getParameterEffectiveState(operator, 'B').effectiveDisabled, true);
  const errors = collectEffectiveRequiredParameterErrors(operator);
  assert.equal(errors.length, 1);
  assert.deepEqual(errors[0].parameterNames, ['A']);

  operator.parameters[1].value = 'active-value';
  assert.deepEqual(collectEffectiveRequiredParameterErrors(operator), []);
});

test('visible hidden and ignored conditions produce reusable effective states', () => {
  const operator = {
    type: 'ModeAware',
    parameterConstraints: [
      {
        parameter: 'AdvancedValue',
        requiredPolicy: 'required',
        visibleWhen: { all: [{ parameter: 'Mode', comparison: 'equals', value: 'Advanced' }] },
        ignoredWhen: { all: [{ parameter: 'Mode', comparison: 'not-equals', value: 'Advanced' }] }
      }
    ],
    parameters: [
      { name: 'Mode', value: 'Basic', defaultValue: 'Basic' },
      { name: 'AdvancedValue', value: '', defaultValue: '' }
    ]
  };

  const basic = getParameterEffectiveState(operator, 'AdvancedValue');
  assert.equal(basic.effectiveVisible, false);
  assert.equal(basic.effectiveIgnored, true);
  assert.equal(basic.effectiveDisabled, true);
  assert.equal(basic.effectiveRequired, false);
  assert.deepEqual(collectEffectiveRequiredParameterErrors(operator), []);

  operator.parameters[0].value = 'Advanced';
  const advanced = getParameterEffectiveState(operator, 'AdvancedValue');
  assert.equal(advanced.effectiveVisible, true);
  assert.equal(advanced.effectiveIgnored, false);
  assert.equal(advanced.effectiveDisabled, false);
  assert.equal(advanced.effectiveRequired, true);
  assert.equal(collectEffectiveRequiredParameterErrors(operator).length, 1);
});

test('output availability uses metadata defaults for old flows and defaults unruled outputs to guaranteed', () => {
  const operator = {
    type: 'Measurement',
    parameters: [
      { name: 'MeasureType', defaultValue: 'PointToPoint' }
    ],
    outputPorts: [
      { name: 'Distance' },
      { name: 'Angle' },
      { name: 'Image' }
    ],
    outputAvailabilityRules: [
      {
        output: 'Distance',
        availableWhen: {
          any: [
            { parameter: 'MeasureType', comparison: 'equals', value: 'PointToPoint' },
            { parameter: 'MeasureType', comparison: 'equals', value: 'PointToLine' }
          ]
        },
        reasonCode: 'MEASUREMENT_DISTANCE_OUTPUT'
      },
      {
        output: 'Angle',
        availableWhen: {
          any: [
            { parameter: 'MeasureType', comparison: 'equals', value: 'LineToLine' },
            { parameter: 'MeasureType', comparison: 'equals', value: 'ThreePointAngle' }
          ]
        },
        reasonCode: 'MEASUREMENT_ANGLE_OUTPUT'
      }
    ]
  };

  const oldFlowStates = getOperatorOutputAvailabilityStates(operator);
  assert.equal(oldFlowStates.get('distance').isAvailable, true);
  assert.equal(oldFlowStates.get('angle').isAvailable, false);
  assert.equal(oldFlowStates.get('image').isAvailable, true);
  assert.equal(oldFlowStates.get('image').isGuaranteed, true);
  assert.equal(oldFlowStates.get('image').reasonCode, 'OUTPUT_ALWAYS_AVAILABLE');

  operator.parameters[0].value = 'ThreePointAngle';
  assert.equal(getOutputAvailabilityState(operator, 'Distance').isAvailable, false);
  assert.equal(getOutputAvailabilityState(operator, 'Angle').isAvailable, true);
});
