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
    rule: 'StringMap',
    defaultOkValue: 'OK',
    defaultNgValue: 'NG'
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
      okValue: 'OK',
      ngValue: 'NG'
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
  assert.equal(panel.appliedConfiguration.finalDecisionBinding.comparator, null);
  assert.equal(panel.appliedConfiguration.finalDecisionBinding.threshold, null);
  assert.equal(panel.appliedConfiguration.missingDecisionPolicy, 'Invalid');
});

test('final decision UI uses the backend 1/0 semantics for JudgmentValue', () => {
  const candidate = {
    operatorId: 'operator-3',
    outputPortId: 'port-3',
    outputName: 'JudgmentValue',
    dataType: 'String',
    rule: 'StringMap',
    defaultOkValue: '1',
    defaultNgValue: '0'
  };
  const panel = createPanel(candidate);

  panel.applySource(panel.candidateKey(candidate));

  assert.equal(panel.appliedConfiguration.finalDecisionBinding.rule, 'StringMap');
  assert.equal(panel.appliedConfiguration.finalDecisionBinding.okValue, '1');
  assert.equal(panel.appliedConfiguration.finalDecisionBinding.ngValue, '0');
});

test('final decision UI leaves open string mappings incomplete without backend defaults', () => {
  const candidate = {
    operatorId: 'operator-4',
    outputPortId: 'port-4',
    outputName: 'Text',
    dataType: 'String',
    rule: 'StringMap'
  };
  const panel = createPanel(candidate);

  panel.applySource(panel.candidateKey(candidate));

  assert.equal(panel.appliedConfiguration.finalDecisionBinding.okValue, null);
  assert.equal(panel.appliedConfiguration.finalDecisionBinding.ngValue, null);
});

test('final decision UI takes Rule and boolean polarity only from the backend candidate', () => {
  const candidate = {
    operatorId: 'operator-5',
    outputPortId: 'port-5',
    outputName: 'IsAnomaly',
    dataType: 'Boolean',
    rule: 'Boolean',
    defaultTrueMeansOk: false
  };
  const panel = createPanel(candidate);

  panel.applySource(panel.candidateKey(candidate));

  assert.equal(panel.appliedConfiguration.finalDecisionBinding.rule, 'Boolean');
  assert.equal(panel.appliedConfiguration.finalDecisionBinding.trueMeansOk, false);
  assert.equal(typeof panel.defaultRule, 'undefined');
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
