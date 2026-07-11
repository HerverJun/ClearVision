import test from 'node:test';
import assert from 'node:assert/strict';

import { FinalDecisionPanel } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/finalDecisionPanel.js';

function createPanel(candidate, current = null) {
  const panel = Object.create(FinalDecisionPanel.prototype);
  panel.validation = { eligibleOutputs: [candidate] };
  panel.getConfiguration = () => current ? structuredClone(current) : null;
  panel.setConfiguration = configuration => {
    panel.appliedConfiguration = structuredClone(configuration);
  };
  return panel;
}

test('final decision UI builds the backend canonical string mapping DTO', () => {
  const candidate = {
    operatorId: 'operator-1',
    operatorName: '结果判定',
    outputPortId: 'port-1',
    outputName: 'JudgmentResult',
    dataType: 'String',
    rule: 'StringMap'
  };
  const panel = createPanel(candidate);

  panel.applySource(panel.candidateKey(candidate));

  assert.deepEqual(panel.appliedConfiguration, {
    finalDecisionBinding: {
      sourceOperatorId: 'operator-1',
      sourceOutputPortId: 'port-1',
      sourceOutputName: 'JudgmentResult',
      dataType: 'String',
      rule: 'StringMap',
      trueMeansOk: true,
      okValue: 'OK',
      ngValue: 'NG',
      comparator: null,
      threshold: null
    },
    missingDecisionPolicy: 'Undetermined'
  });
});

test('final decision UI builds numeric comparison rules without a parallel frontend model', () => {
  const candidate = {
    operatorId: 'operator-2',
    outputPortId: 'port-2',
    outputName: 'Score',
    dataType: 'Float',
    rule: 'NumericComparison'
  };
  const panel = createPanel(candidate, { missingDecisionPolicy: 'Invalid' });

  panel.applySource(panel.candidateKey(candidate));

  assert.equal(panel.appliedConfiguration.finalDecisionBinding.rule, 'NumericComparison');
  assert.equal(panel.appliedConfiguration.finalDecisionBinding.comparator, 'GreaterThanOrEqual');
  assert.equal(panel.appliedConfiguration.finalDecisionBinding.threshold, 0);
  assert.equal(panel.appliedConfiguration.missingDecisionPolicy, 'Invalid');
});

test('final decision UI clears unresolved selections instead of inventing a binding', () => {
  const candidate = {
    operatorId: 'operator-1',
    outputPortId: 'port-1',
    outputName: 'Decision',
    dataType: 'Boolean'
  };
  const panel = createPanel(candidate, { missingDecisionPolicy: 'NotApplicable' });

  panel.applySource('missing:port');

  assert.equal(panel.appliedConfiguration.finalDecisionBinding, null);
  assert.equal(panel.appliedConfiguration.missingDecisionPolicy, 'NotApplicable');
});
