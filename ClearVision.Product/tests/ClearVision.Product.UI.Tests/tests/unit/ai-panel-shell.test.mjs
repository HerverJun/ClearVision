import test from 'node:test';
import assert from 'node:assert/strict';
import { aiPanelShellTestApi } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelShellPresentation.js';

test('AI shell stays idle only when the canonical projection is idle', () => {
  assert.equal(aiPanelShellTestApi.isActiveProjection({ phase: 'idle' }), false);
  assert.equal(aiPanelShellTestApi.isActiveProjection({ phase: 'routing' }), true);
  assert.equal(aiPanelShellTestApi.isActiveProjection({ phase: 'ready_to_build' }), true);
});

test('AI shell reads task title from canonical workspace state', () => {
  assert.equal(aiPanelShellTestApi.readTaskTitle(null, {
    plan: { goal: '检测端子线序' },
  }), '检测端子线序');
  assert.equal(aiPanelShellTestApi.readTaskTitle(null, {
    plan: null,
    intent: { description: '检查包装箱外观' },
  }), '检查包装箱外观');
});

test('AI shell blocker count only includes unresolved blocking projection items', () => {
  assert.equal(aiPanelShellTestApi.countReliableBlockers({
    clarificationQueue: [
      { blocksBuild: true, answered: false, deferred: false },
      { blocksBuild: true, answered: true, deferred: false },
      { blocksBuild: true, answered: false, deferred: true },
      { blocksBuild: false, answered: false, deferred: false },
    ],
  }), 1);
});

test('AI shell next step prefers canonical readiness projection text', () => {
  assert.equal(aiPanelShellTestApi.readNextStep({
    plan: { nextAction: '计划建议' },
  }, {
    readiness: { primaryMessage: '请先确认图像来源' },
  }), '请先确认图像来源');
});
