import { spawnSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const phase = process.argv[2];
if (phase !== 'reference' && phase !== 'candidate') {
  throw new Error('Usage: node option-d-g3-gate.mjs <reference|candidate>');
}

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const uiTestsRoot = resolve(scriptDirectory, '../../..');
const workspaceRoot = resolve(uiTestsRoot, '../../..');
const metricsScript = resolve(scriptDirectory, 'option-d-g3-master-metrics.mjs');
const masterComparisonScript = resolve(scriptDirectory, 'option-d-g3-master-compare.mjs');
const playwrightCli = resolve(uiTestsRoot, 'node_modules/@playwright/test/cli.js');
const evidenceRoot = resolve(uiTestsRoot, '../../../.tmp/studio-ui-next/option-d-g3');
const visualEvidenceRoot = resolve(evidenceRoot, 'visual');
const candidateManifestPath = resolve(visualEvidenceRoot, 'candidate.json');
const masterRoot = resolve(workspaceRoot, '_visual_master/option_D/screens');
const candidateRoot = resolve(visualEvidenceRoot, 'candidate');
const masterComparisonRoot = resolve(evidenceRoot, 'master-comparison');
const masterComparisonManifestPath = resolve(masterComparisonRoot, 'manifest.json');
const gateInvocationId = randomUUID();
const sha256Pattern = /^[a-f0-9]{64}$/;
const expectedCaptures = Object.freeze({
  'd02-overview-1920x1080': Object.freeze({ screen: 'D02', state: 'overview', route: '/overview', master: '02_overview.png' }),
  'd03-projects-data-1920x1080': Object.freeze({ screen: 'D03', state: 'projects-data', route: '/projects', master: '03_projects_data.png' }),
  'd04-projects-empty-1920x1080': Object.freeze({ screen: 'D04', state: 'projects-empty', route: '/projects', master: '04_projects_empty.png' }),
  'd15-operators-1920x1080': Object.freeze({ screen: 'D15', state: 'operators', route: '/operators', master: '15_operator_catalog.png' }),
  'd22-diagnostics-1920x1080': Object.freeze({ screen: 'D22', state: 'diagnostics', route: '/diagnostics', master: '22_diagnostics.png' }),
  'd23-about-1920x1080': Object.freeze({ screen: 'D23', state: 'about', route: '/about', master: '23_about.png' })
});
const expectedMasterSha256 = Object.freeze({
  D02: 'a6a902196b5486817f80c094d469fa4d96e8c934fb2a36c5e7947fc3d5f24769',
  D03: 'fe6d5e6c368573de83d0a6a0ed46148a2f0e6f01d9e002565c63c6d4047c5e94',
  D04: 'a0117dcd2b62a5cef6c499f4e0f658a4c3de255ae3da783640f6d6952f030087',
  D15: 'a01ee6cfbcd1344c2340ce18ced5eb2cfce66450dd0509f5e87ccedead3a2d1f',
  D22: '0415729663e19fb6b2527956eec74d9f64284cefa8b1ba7be7f8c4d5c9e6ee97',
  D23: '4b085e25511b6fcffc72d6d6af33c39574baa9bfb7106729485b4035a77c8e84'
});

run(process.execPath, [metricsScript]);
run(process.execPath, [
  playwrightCli,
  'test',
  'tests/e2e/studio-ui-next/option-d-g3-visual.spec.ts',
  '--workers=1',
  '--reporter=list'
], {
  CV_UI_SCENARIO: 'studio-ui-next',
  CV_OPTION_D_G3_VISUAL_PHASE: phase,
  CV_OPTION_D_G3_GATE_INVOCATION_ID: gateInvocationId
});
validateMetrics();
validateVisualManifest();
if (phase === 'candidate') {
  const comparisonExitCode = run(process.execPath, [masterComparisonScript], {}, [0, 2]);
  const comparisonResult = validateMasterComparison();
  const expectedExitCode = comparisonResult === 'PASS' ? 0 : 2;
  if (comparisonExitCode !== expectedExitCode) {
    throw new Error(`G3 raw Master comparison exit/result mismatch: exit ${comparisonExitCode}, result ${comparisonResult}.`);
  }
  if (comparisonResult !== 'PASS') {
    throw new Error(`G3 raw Master pixel gate failed; see ${masterComparisonManifestPath}`);
  }
}

function validateMetrics() {
  const path = resolve(evidenceRoot, 'master-measurements.json');
  const manifest = JSON.parse(readFileSync(path, 'utf8'));
  const measurements = Array.isArray(manifest.measurements) ? manifest.measurements : [];
  const ids = measurements.map(measurement => measurement.id).sort();
  const expectedIds = Object.keys(expectedMasterSha256).sort();
  if (manifest.schemaVersion !== 2
    || manifest.fixtureId !== 'option-d-g3-master-measurements.v2'
    || manifest.visualAuthority !== '_visual_master/option_D/screens'
    || manifest.assertionResult !== 'PASS'
    || JSON.stringify(ids) !== JSON.stringify(expectedIds)
    || measurements.some(measurement => measurement.sha256 !== expectedMasterSha256[measurement.id])) {
    throw new Error(`G3 master measurement postcondition failed: ${path}`);
  }
}

function validateVisualManifest() {
  const path = resolve(visualEvidenceRoot, `${phase}.json`);
  const manifest = JSON.parse(readFileSync(path, 'utf8'));
  const captures = Array.isArray(manifest.captures) ? manifest.captures : [];
  const expectedIds = Object.keys(expectedCaptures).sort();
  if (manifest.schemaVersion !== 2
    || manifest.gateInvocationId !== gateInvocationId
    || manifest.fixtureId !== 'option-d-g3-read-surfaces.v1'
    || manifest.approvedFixture !== 'option-d-g0-deterministic.v1'
    || manifest.dataSource !== 'OPTION_D_G3_FIXTURE'
    || manifest.visualAuthority !== '_visual_master/option_D/screens'
    || manifest.visualPhase !== phase
    || manifest.referenceSealStatus !== 'FROZEN'
    || manifest.complete !== true
    || manifest.maskPolicy !== 'NO_MASKS'
    || manifest.canonicalCssViewport?.width !== 1920
    || manifest.canonicalCssViewport?.height !== 1080
    || manifest.deviceScaleFactor !== 2
    || manifest.theme !== 'light'
    || manifest.density !== 'compact'
    || manifest.thresholds?.perChannelDelta !== 8
    || manifest.thresholds?.maxChangedPixelRatio !== 0.01
    || manifest.thresholds?.masterAnchorToleranceCssPixels !== 1
    || manifest.thresholds?.productRuntimeBaselineQueryOwnerCount !== 1
    || captures.length !== expectedIds.length) {
    throw new Error(`G3 ${phase} visual manifest failed its postcondition: ${path}`);
  }
  const actualIds = captures.map(capture => capture.id).sort();
  if (JSON.stringify(actualIds) !== JSON.stringify(expectedIds)) {
    throw new Error(`G3 ${phase} visual manifest has incomplete capture identities.`);
  }
  if (captures.some(capture => !captureIsValid(capture))) {
    throw new Error(`G3 ${phase} visual manifest has invalid functional, geometry, or cleanup evidence.`);
  }
  assertProjectsFamilyGeometry(captures);
  if (phase === 'candidate' && captures.some(capture => !capture.comparison
    || !Number.isFinite(capture.comparison.changedPixelRatio)
    || capture.comparison.changedPixelRatio < 0
    || capture.comparison.changedPixelRatio > 0.01
    || !isArtifactHash(resolve(visualEvidenceRoot, 'reference', `${capture.id}.png`), capture.referenceSha256)
    || !isArtifactHash(capture.diff, capture.diffSha256)
    || !isArtifactHash(capture.overlay, capture.overlaySha256))) {
    throw new Error('G3 candidate visual manifest is missing or failed whole-image comparison evidence.');
  }
}

function validateMasterComparison() {
  const manifest = JSON.parse(readFileSync(masterComparisonManifestPath, 'utf8'));
  const candidateManifestBuffer = readFileSync(candidateManifestPath);
  const candidateManifest = JSON.parse(candidateManifestBuffer.toString('utf8'));
  const candidateCaptures = new Map(candidateManifest.captures.map(capture => [capture.id, capture]));
  const captures = Array.isArray(manifest.captures) ? manifest.captures : [];
  const expectedIds = Object.keys(expectedCaptures).sort();
  const actualIds = captures.map(capture => capture.id).sort();
  if (manifest.schemaVersion !== 2
    || manifest.fixtureId !== 'option-d-g3-master-candidate-comparison.v2'
    || manifest.visualAuthority !== '_visual_master/option_D/screens'
    || manifest.candidateAuthority !== '.tmp/studio-ui-next/option-d-g3/visual/candidate'
    || manifest.candidateManifestPath !== candidateManifestPath
    || manifest.candidateManifestSha256 !== sha256(candidateManifestBuffer)
    || manifest.candidateGateInvocationId !== gateInvocationId
    || manifest.candidateGateInvocationId !== candidateManifest.gateInvocationId
    || manifest.maskPolicy !== 'NO_MASKS'
    || manifest.expectedOutput?.width !== 3840
    || manifest.expectedOutput?.height !== 2160
    || manifest.thresholds?.perChannelDelta !== 8
    || manifest.thresholds?.maxChangedPixelRatio !== 0.01
    || manifest.thresholds?.minimumSsim !== 0.99
    || manifest.ssimMethod !== 'GLOBAL_LUMINANCE_DIAGNOSTIC'
    || (manifest.assertionResult !== 'PASS' && manifest.assertionResult !== 'FAIL')
    || JSON.stringify(actualIds) !== JSON.stringify(expectedIds)) {
    throw new Error(`G3 raw Master comparison manifest failed its postcondition: ${masterComparisonManifestPath}`);
  }
  for (const capture of captures) {
    const expected = expectedCaptures[capture.id];
    const candidateCapture = candidateCaptures.get(capture.id);
    const expectedMasterPath = resolve(masterRoot, expected.master);
    const expectedCandidatePath = resolve(candidateRoot, `${capture.id}.png`);
    const expectedDiffPath = resolve(masterComparisonRoot, `${capture.id}.master.diff.png`);
    const expectedOverlayPath = resolve(masterComparisonRoot, `${capture.id}.master.overlay.png`);
    const expectedResult = capture.changedPixelRatio <= 0.01 && capture.ssim >= 0.99 ? 'PASS' : 'FAIL';
    if (!candidateCapture
      || capture.screen !== expected.screen
      || capture.master !== expected.master
      || capture.masterPath !== expectedMasterPath
      || capture.masterSha256 !== expectedMasterSha256[expected.screen]
      || !isArtifactHash(capture.masterPath, capture.masterSha256)
      || capture.candidatePath !== expectedCandidatePath
      || capture.candidateSha256 !== candidateCapture.sha256
      || !isArtifactHash(capture.candidatePath, capture.candidateSha256)
      || capture.width !== 3840
      || capture.height !== 2160
      || !Number.isInteger(capture.changedPixels)
      || capture.changedPixels < 0
      || capture.changedPixels > capture.width * capture.height
      || !Number.isFinite(capture.changedPixelRatio)
      || Math.abs(capture.changedPixelRatio - capture.changedPixels / (capture.width * capture.height)) > 1e-12
      || !Number.isFinite(capture.maxChannelDelta)
      || !Number.isFinite(capture.meanAbsoluteChannelDelta)
      || !Number.isFinite(capture.ssim)
      || capture.result !== expectedResult
      || capture.diffPath !== expectedDiffPath
      || !isArtifactHash(capture.diffPath, capture.diffSha256)
      || capture.overlayPath !== expectedOverlayPath
      || !isArtifactHash(capture.overlayPath, capture.overlaySha256)) {
      throw new Error(`${capture.screen} raw Master comparison binding is invalid.`);
    }
  }
  const expectedAssertion = captures.every(capture => capture.result === 'PASS') ? 'PASS' : 'FAIL';
  if (manifest.assertionResult !== expectedAssertion) {
    throw new Error('G3 raw Master comparison assertion summary is inconsistent.');
  }
  return manifest.assertionResult;
}

function captureIsValid(capture) {
  const expected = expectedCaptures[capture.id];
  const anchors = Array.isArray(capture.geometry?.anchors) ? capture.geometry.anchors : [];
  const masthead = anchors.find(anchor => anchor.id === 'masthead-end');
  return Boolean(expected)
    && capture.screen === expected.screen
    && capture.state === expected.state
    && capture.route === expected.route
    && capture.phase === phase
    && capture.cssViewport?.width === 1920
    && capture.cssViewport?.height === 1080
    && capture.width === 3840
    && capture.height === 2160
    && capture.masterSha256 === expectedMasterSha256[capture.screen]
    && capture.functionalAssertions === 'PASS'
    && capture.functionalAudit?.result === 'PASS'
    && Array.isArray(capture.functionalAudit?.regionsConfirmed)
    && Array.isArray(capture.functionalAudit?.controlsConfirmed)
    && Array.isArray(capture.functionalAudit?.forbiddenAdditionsChecked)
    && capture.ownerCleanup?.result === 'PASS'
    && ownerCleanupIsValid(capture.ownerCleanup)
    && capture.geometry?.horizontalOverflow === 0
    && capture.geometry?.verticalOverflow === 0
    && capture.geometry?.contentHorizontalOverflow === 0
    && capture.geometry?.pageHorizontalOverflow === 0
    && capture.geometry?.mainCount === 1
    && capture.geometry?.topbarPageOverlap === 0
    && anchors.length > 0
    && anchors.every(anchor => Number.isFinite(anchor.actualCssPixel)
      && Number.isFinite(anchor.expectedCssPixel)
      && Number.isFinite(anchor.deltaCssPixels)
      && anchor.toleranceCssPixels === 1
      && Math.abs(anchor.deltaCssPixels) <= 1
      && anchor.withinTolerance === true)
    && masthead?.authority === 'G2_PRODUCT_SHELL'
    && masthead?.expectedCssPixel === 74
    && Array.isArray(capture.requestAudit)
    && capture.requestAudit.length > 0
    && capture.requestAudit.every(request => request.method === 'GET'
      && request.handledAs !== 'UNHANDLED_FAIL_CLOSED')
    && Array.isArray(capture.runtimeErrors)
    && capture.runtimeErrors.length === 0
    && capture.theme === 'light'
    && capture.density === 'compact'
    && typeof capture.fontFamily === 'string'
    && capture.fontFamily.includes('Segoe UI')
    && capture.screenshot === resolve(visualEvidenceRoot, phase, `${capture.id}.png`)
    && isArtifactHash(capture.screenshot, capture.sha256);
}

function ownerCleanupIsValid(evidence) {
  const querySnapshots = [evidence.firstUnmount, evidence.secondUnmount];
  const lifecycleSnapshots = [
    evidence.initialProjectLifecycle,
    evidence.firstUnmountProjectLifecycle,
    evidence.remountProjectLifecycle,
    evidence.secondUnmountProjectLifecycle
  ];
  return querySnapshots.every(snapshot => snapshot?.activeOwnerCount === 1
      && snapshot?.activeRequestCount === 0)
    && lifecycleSnapshots.every(snapshot => snapshot?.ownerCount === 1
      && snapshot?.activeAbortControllerCount === 0
      && snapshot?.inFlightCommandCount === 0
      && snapshot?.disposed === false);
}

function assertProjectsFamilyGeometry(captures) {
  const populated = captures.find(capture => capture.screen === 'D03')?.geometry;
  const empty = captures.find(capture => capture.screen === 'D04')?.geometry;
  if (!populated || !empty) throw new Error('G3 projects family geometry is incomplete.');
  for (const path of [
    ['page', 'x'], ['page', 'right'],
    ['familySurface', 'x'], ['familySurface', 'y'], ['familySurface', 'right'],
    ['familyToolbar', 'x'], ['familyToolbar', 'y'], ['familyToolbar', 'right'], ['familyToolbar', 'bottom'],
    ['familyFooter', 'x'], ['familyFooter', 'y'], ['familyFooter', 'right'], ['familyFooter', 'bottom']
  ]) {
    const populatedValue = populated[path[0]]?.[path[1]];
    const emptyValue = empty[path[0]]?.[path[1]];
    if (!Number.isFinite(populatedValue)
      || !Number.isFinite(emptyValue)
      || Math.abs(populatedValue - emptyValue) > 1) {
      throw new Error(`G3 D03/D04 family geometry differs at ${path.join('.')}.`);
    }
  }
}

function isArtifactHash(path, expectedHash) {
  return typeof path === 'string'
    && typeof expectedHash === 'string'
    && sha256Pattern.test(expectedHash)
    && existsSync(path)
    && createHash('sha256').update(readFileSync(path)).digest('hex') === expectedHash;
}

function run(executable, args, extraEnv = {}, acceptedStatuses = [0]) {
  const result = spawnSync(executable, args, {
    cwd: uiTestsRoot,
    env: { ...process.env, ...extraEnv },
    stdio: 'inherit',
    windowsHide: true
  });
  if (result.error) throw result.error;
  if (!acceptedStatuses.includes(result.status)) {
    throw new Error(`${executable} exited with code ${result.status ?? 'unknown'}.`);
  }
  return result.status;
}

function sha256(buffer) {
  return createHash('sha256').update(buffer).digest('hex');
}
