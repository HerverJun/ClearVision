import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const phase = process.argv[2];
if (phase !== 'reference' && phase !== 'candidate') {
  throw new Error('Usage: node option-d-g1-gate.mjs <reference|candidate>');
}

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const uiTestsRoot = resolve(scriptDirectory, '../../..');
const metricsScript = resolve(scriptDirectory, 'option-d-g1-master-metrics.mjs');
const playwrightCli = resolve(uiTestsRoot, 'node_modules/@playwright/test/cli.js');
const visualEvidenceRoot = resolve(
  uiTestsRoot,
  '../../../.tmp/studio-ui-next/option-d-g1/visual'
);
const gateInvocationId = randomUUID();
const gateSpecs = [
  'tests/e2e/studio-ui-next/design-foundation.spec.ts',
  'tests/e2e/studio-ui-next/canvas-foundation.spec.ts',
  'tests/e2e/studio-ui-next/option-d-g1-visual.spec.ts'
];

run(process.execPath, [metricsScript]);
run(process.execPath, [playwrightCli, 'test', ...gateSpecs, '--workers=1', '--reporter=list'], {
  CV_UI_SCENARIO: 'studio-ui-next',
  CV_OPTION_D_G1_VISUAL_PHASE: phase,
  CV_OPTION_D_G1_GATE_INVOCATION_ID: gateInvocationId
});
validateVisualManifest();

function validateVisualManifest() {
  const manifestPath = resolve(visualEvidenceRoot, `${phase}.json`);
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
  const captures = Array.isArray(manifest.captures) ? manifest.captures : [];
  if (manifest.schemaVersion !== 2
    || manifest.gateInvocationId !== gateInvocationId
    || manifest.visualPhase !== phase
    || manifest.complete !== true
    || manifest.maskPolicy !== 'NO_MASKS'
    || captures.length !== 4) {
    throw new Error(`G1 ${phase} visual manifest failed its postcondition: ${manifestPath}`);
  }
  const expectedIds = [
    'canvas-dark-compact',
    'canvas-light-compact',
    'design-dark-compact',
    'design-light-compact'
  ];
  const actualIds = captures.map(capture => capture.id).sort();
  if (JSON.stringify(actualIds) !== JSON.stringify(expectedIds)) {
    throw new Error(`G1 ${phase} visual manifest has incomplete capture identities.`);
  }
  if (captures.some(capture => capture.phase !== phase
    || capture.width !== 3840
    || capture.height !== 2160
    || typeof capture.sha256 !== 'string')) {
    throw new Error(`G1 ${phase} visual manifest has invalid capture evidence.`);
  }
  if (phase === 'candidate' && captures.some(capture => !capture.comparison
    || typeof capture.referenceSha256 !== 'string'
    || typeof capture.diffSha256 !== 'string'
    || typeof capture.overlaySha256 !== 'string')) {
    throw new Error('G1 candidate visual manifest is missing comparison evidence.');
  }
}

function run(executable, args, extraEnv = {}) {
  const result = spawnSync(executable, args, {
    cwd: uiTestsRoot,
    env: { ...process.env, ...extraEnv },
    stdio: 'inherit',
    windowsHide: true
  });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(`${executable} exited with code ${result.status ?? 'unknown'}.`);
  }
}
