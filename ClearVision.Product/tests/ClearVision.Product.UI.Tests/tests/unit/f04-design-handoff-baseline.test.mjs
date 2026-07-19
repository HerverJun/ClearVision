import assert from 'node:assert/strict';
import test from 'node:test';
import {
  f04DesignHandoffAutomatedScenes,
  f04DesignHandoffDeferredStates,
  f04DesignHandoffPlaywrightGrep,
  validateF04DesignHandoffCatalog
} from '../e2e/studio-ui-next/f04-design-handoff-baseline.mjs';

test('F04 design handoff catalog keeps deterministic unique screenshot identities', () => {
  assert.equal(validateF04DesignHandoffCatalog(), true);
  assert.ok(f04DesignHandoffAutomatedScenes.length >= 25);
  assert.equal(new Set(f04DesignHandoffAutomatedScenes.map(item => item.id)).size, f04DesignHandoffAutomatedScenes.length);
  assert.ok(f04DesignHandoffAutomatedScenes.some(item => item.width === 1920 && item.height === 1080));
  assert.ok(f04DesignHandoffAutomatedScenes.some(item => item.width === 1350 && item.height === 704));
  assert.ok(f04DesignHandoffAutomatedScenes.some(item => item.density === 'compact'));
  assert.ok(f04DesignHandoffAutomatedScenes.some(item => item.density === 'comfortable'));
});

test('F04 design handoff catalog names unavailable product states instead of fabricating screenshots', () => {
  const deferredIds = new Set(f04DesignHandoffDeferredStates.map(item => item.id));
  assert.ok(deferredIds.has('operator-flyout-expanded'));
  assert.ok(deferredIds.has('final-decision-unconfigured-valid-invalid'));
  assert.ok(deferredIds.has('global-variables-entry'));
  assert.ok(deferredIds.has('canvas-node-running-success-failed-business-ng'));
  assert.match(f04DesignHandoffPlaywrightGrep, /Workspace Shell fits/);
  assert.match(f04DesignHandoffPlaywrightGrep, /complex flow/);
});
