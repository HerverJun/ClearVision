import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildWireSequenceFollowupHint,
  createWireSequenceParameterPatch
} from '../../../../src/Acme.Product.Desktop/wwwroot/src/features/flow-editor/wireSequenceAssist.js';

test('createWireSequenceParameterPatch resolves upstream BoxNms for legacy flows', () => {
  const flowData = {
    operators: [
      { id: 'acq', type: 'ImageAcquisition' },
      { id: 'nms', type: 'BoxNms' },
      { id: 'judge', type: 'DetectionSequenceJudge' }
    ],
    connections: [
      { sourceOperatorId: 'acq', targetOperatorId: 'nms' },
      { sourceOperatorId: 'nms', targetOperatorId: 'judge' }
    ]
  };

  assert.deepEqual(
    createWireSequenceParameterPatch(flowData, 'judge', {
      'BoxNms.ScoreThreshold': 0.2,
      'BoxNms.IouThreshold': 0.4,
      'ExpectedLabels': 'do-not-apply'
    }),
    {
      operatorId: 'nms',
      parameters: {
        ScoreThreshold: 0.2,
        IouThreshold: 0.4
      }
    }
  );
});

test('createWireSequenceParameterPatch resolves upstream DeepLearning confidence', () => {
  const flowData = {
    operators: [
      { id: 'acq', type: 'ImageAcquisition' },
      { id: 'dl', type: 'DeepLearning' },
      { id: 'judge', type: 'DetectionSequenceJudge' }
    ],
    connections: [
      { sourceOperatorId: 'acq', targetOperatorId: 'dl' },
      { sourceOperatorId: 'dl', targetOperatorId: 'judge' }
    ]
  };

  assert.deepEqual(
    createWireSequenceParameterPatch(flowData, 'judge', {
      'DeepLearning.Confidence': 0.1,
      'ExpectedLabels': 'do-not-apply'
    }),
    {
      operatorId: 'dl',
      parameters: {
        Confidence: 0.1
      }
    }
  );
});

test('buildWireSequenceFollowupHint keeps parameter-only boundary explicit', () => {
  const hint = buildWireSequenceFollowupHint({
    scenarioKey: 'wire-sequence-terminal',
    diagnosticCodes: ['duplicate_detected_class', 'low_detection_confidence'],
    finalParameters: {
      'DeepLearning.Confidence': 0.1
    }
  });

  assert.match(hint, /只允许调整参数/);
  assert.match(hint, /DeepLearning\.Confidence = 0\.1/);
  assert.match(hint, /不要改写 ExpectedLabels、ExpectedCount、ModelPath、LabelsPath/);
});
