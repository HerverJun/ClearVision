import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { audit, validateAudit } from '../../scripts/audit-ai-css.mjs';

const testDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(testDir, '../../../../..');

test('AI CSS governance audit stays within the frozen baseline', () => {
  assert.deepEqual(validateAudit(), []);
  assert.ok(audit.legacyLineCount < 6456);
  assert.ok(audit.totalLineCount >= audit.legacyLineCount);
});

test('AI style modules are loaded in the declared governance order', () => {
  const indexSource = fs.readFileSync(
    path.join(repoRoot, 'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/index.html'),
    'utf8'
  );
  const entries = [
    'ai-panel/shell.css',
    'ai-panel/conversation.css',
    'ai-panel/plan.css',
    'ai-panel/build-validation.css',
    'ai-panel/application.css',
    'ai-panel/details.css',
    'ai-panel/responsive.css'
  ];
  let previous = indexSource.indexOf('src/shared/styles/ai-panel.css');
  assert.ok(previous >= 0);
  for (const entry of entries) {
    const current = indexSource.indexOf(entry);
    assert.ok(current > previous, `${entry} should load after the previous AI stylesheet entry`);
    previous = current;
  }
});
