import { spawnSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const phase = process.argv[2];
if (phase !== 'reference' && phase !== 'candidate') {
  throw new Error('Usage: node option-d-g2-gate.mjs <reference|candidate>');
}

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const uiTestsRoot = resolve(scriptDirectory, '../../..');
const metricsScript = resolve(scriptDirectory, 'option-d-g2-master-metrics.mjs');
const playwrightCli = resolve(uiTestsRoot, 'node_modules/@playwright/test/cli.js');
const evidenceRoot = resolve(uiTestsRoot, '../../../.tmp/studio-ui-next/option-d-g2');
const visualEvidenceRoot = resolve(evidenceRoot, 'visual');
const gateInvocationId = randomUUID();
const expectedIds = [
  'd01-login-1366x768',
  'd01-login-1536x864',
  'd01-login-1920x1080',
  'd24-forbidden-1366x768',
  'd24-forbidden-1536x864',
  'd24-forbidden-1920x1080'
];
const sha256Pattern = /^[a-f0-9]{64}$/;

run(process.execPath, [metricsScript]);
run(process.execPath, [
  playwrightCli,
  'test',
  'tests/e2e/studio-ui-next/option-d-g2-visual.spec.ts',
  '--workers=1',
  '--reporter=list'
], {
  CV_UI_SCENARIO: 'studio-ui-next',
  CV_OPTION_D_G2_VISUAL_PHASE: phase,
  CV_OPTION_D_G2_GATE_INVOCATION_ID: gateInvocationId
});
validateMetrics();
validateVisualManifest();

function validateMetrics() {
  const path = resolve(evidenceRoot, 'master-measurements.json');
  const manifest = JSON.parse(readFileSync(path, 'utf8'));
  const ids = Array.isArray(manifest.measurements)
    ? manifest.measurements.map(measurement => measurement.id).sort()
    : [];
  if (manifest.schemaVersion !== 2
    || manifest.fixtureId !== 'option-d-g2-master-measurements.v2'
    || manifest.visualAuthority !== '_visual_master/option_D/screens'
    || manifest.assertionResult !== 'PASS'
    || JSON.stringify(ids) !== JSON.stringify(['D01', 'D24'])) {
    throw new Error(`G2 master measurement postcondition failed: ${path}`);
  }
}

function validateVisualManifest() {
  const path = resolve(visualEvidenceRoot, `${phase}.json`);
  const manifest = JSON.parse(readFileSync(path, 'utf8'));
  const captures = Array.isArray(manifest.captures) ? manifest.captures : [];
  if (manifest.schemaVersion !== 2
    || manifest.gateInvocationId !== gateInvocationId
    || manifest.visualPhase !== phase
    || manifest.referenceSealStatus !== 'FROZEN'
    || manifest.complete !== true
    || manifest.maskPolicy !== 'NO_MASKS'
    || captures.length !== expectedIds.length) {
    throw new Error(`G2 ${phase} visual manifest failed its postcondition: ${path}`);
  }
  const actualIds = captures.map(capture => capture.id).sort();
  if (JSON.stringify(actualIds) !== JSON.stringify(expectedIds)) {
    throw new Error(`G2 ${phase} visual manifest has incomplete capture identities.`);
  }
  if (captures.some(capture => capture.phase !== phase
    || capture.width !== capture.cssViewport.width * 2
    || capture.height !== capture.cssViewport.height * 2
    || capture.functionalAssertions !== 'PASS'
    || capture.geometry?.horizontalOverflow !== 0
    || capture.geometry?.verticalOverflow !== 0
    || capture.geometry?.stageHorizontalOverflow !== 0
    || capture.geometry?.stageVerticalOverflow !== 0
    || capture.geometry?.mainCount !== 1
    || capture.geometry?.frameMastheadOverlap !== 0
    || !Array.isArray(capture.requestAudit)
    || !Array.isArray(capture.runtimeErrors)
    || capture.runtimeErrors.length !== 0
    || !isArtifactHash(capture.screenshot, capture.sha256))) {
    throw new Error(`G2 ${phase} visual manifest has invalid functional or geometry evidence.`);
  }
  if (phase === 'candidate' && captures.some(capture => !capture.comparison
    || !Number.isFinite(capture.comparison.changedPixelRatio)
    || capture.comparison.changedPixelRatio < 0
    || capture.comparison.changedPixelRatio > 0.01
    || !isArtifactHash(resolve(visualEvidenceRoot, 'reference', `${capture.id}.png`), capture.referenceSha256)
    || !isArtifactHash(capture.diff, capture.diffSha256)
    || !isArtifactHash(capture.overlay, capture.overlaySha256))) {
    throw new Error('G2 candidate visual manifest is missing or failed whole-image comparison evidence.');
  }
}

function isArtifactHash(path, expectedHash) {
  return typeof path === 'string'
    && typeof expectedHash === 'string'
    && sha256Pattern.test(expectedHash)
    && existsSync(path)
    && createHash('sha256').update(readFileSync(path)).digest('hex') === expectedHash;
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
