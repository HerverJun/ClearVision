import test from 'node:test';
import assert from 'node:assert/strict';
import {
  collectEffectiveRequiredParameterErrors,
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
      requiredWhen: { all: [{ parameter: 'SourceType', comparison: 'equals', value: 'Camera' }] },
      disabledWhen: { all: [{ parameter: 'SourceType', comparison: 'equals', value: 'File' }] },
      atLeastOneGroup: 'image-camera-source',
      aliasFor: 'CameraId'
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

test('canonical parameter value wins over aliases and aliases win over metadata defaults', () => {
  const aliasOnly = acquisition([
    { name: 'SourceType', value: 'Camera', defaultValue: 'File' },
    { name: 'CameraId', value: '', defaultValue: '' },
    { name: 'CameraBindingId', value: 'binding-camera' }
  ]);
  assert.equal(getOperatorParameterValue(aliasOnly, 'CameraId'), 'binding-camera');
  assert.equal(getOperatorParameterValue(aliasOnly, 'CameraBindingId'), 'binding-camera');

  const legacyAlias = acquisition([
    { name: 'SourceType', value: 'Camera', defaultValue: 'File' },
    { name: 'CameraId', value: '', defaultValue: '' },
    { name: 'cameraId', value: 'legacy-camera' }
  ]);
  assert.equal(getOperatorParameterValue(legacyAlias, 'CameraId'), 'legacy-camera');

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
  assert.equal(pendingErrors[0].kind, 'atLeastOneOf');

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
