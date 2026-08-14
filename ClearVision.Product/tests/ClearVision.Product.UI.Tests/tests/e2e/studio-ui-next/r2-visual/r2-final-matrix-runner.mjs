import { createHash } from 'node:crypto';
import { execFileSync, spawnSync } from 'node:child_process';
import {
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  rmSync,
  statSync,
  symlinkSync,
  writeFileSync
} from 'node:fs';
import { connect } from 'node:net';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  computeCandidateContentHash,
  computeFixtureHash,
  findRepositoryRoot,
  readJson,
  validateAgainstSchema
} from './validate-r2-evidence.mjs';
import { inspectPngBuffer } from './r2-png.mjs';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = findRepositoryRoot(scriptDirectory);
const uiTestsRoot = resolve(scriptDirectory, '../../../..');
const studioUiRelative = 'ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI';
const canonicalWwwrootRelative = 'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot';
const studioUiRoot = resolve(repositoryRoot, studioUiRelative);
const matrixConfig = resolve(scriptDirectory, 'playwright.r2-matrix.config.ts');
const playwrightCli = resolve(uiTestsRoot, 'node_modules/@playwright/test/cli.js');
const summarySchema = readJson(resolve(scriptDirectory, 'r2-final-matrix-summary.schema.json'));
const approvedWritesContract = readJson(resolve(scriptDirectory, 'r2-approved-writes.json'));
const approvedWritesByScene = approvedWritesContract.scenes;
const s05BitmapContract = readJson(resolve(scriptDirectory, '../f03-preview-bitmap-contract.json'));
const variants = Object.freeze(['B0', 'B2', 'EXCEPTION']);
const expectedGroups = Object.freeze(Array.from({ length: 14 }, (_, index) =>
  variants.map(variant => `S${String(index).padStart(2, '0')}-${variant}`)).flat());
const phaseFiles = Object.freeze([
  'before.png',
  'after.png',
  'dom-before.json',
  'dom-after.json',
  'interaction-before.json',
  'interaction-final.json',
  'capture-before.json',
  'capture-final.json'
]);
const s05SourceFiles = Object.freeze(['source-before.png', 'source-final.png']);

export async function runFinalMatrix(options = {}) {
  const runId = options.runId ?? `r2-final-${new Date().toISOString().replace(/[-:]/g, '').replace(/\..+/, 'z')}`;
  if (!/^[a-z0-9][a-z0-9._-]*$/i.test(runId)) throw new Error(`Invalid R2 matrix runId: ${runId}.`);
  const beforePort = options.beforePort ?? 5319;
  const finalPort = options.finalPort ?? 5318;
  if (beforePort === finalPort) throw new Error('R2 matrix before and final ports must be different.');
  const runRoot = resolve(repositoryRoot, '.tmp/studio-ui-next/view-polish-r2/R2.7', runId);
  const workRoot = resolve(repositoryRoot, '.tmp/studio-ui-next/r2/dual-run', runId);
  if (existsSync(runRoot) || existsSync(workRoot)) {
    throw new Error(`R2 matrix runId already exists and will not be merged or overwritten: ${runId}.`);
  }
  await assertPortReleased(beforePort, 'before');
  await assertPortReleased(finalPort, 'final');

  const headSha = git(['rev-parse', 'HEAD']).trim().toLowerCase();
  const candidateContentHash = computeCandidateContentHash(repositoryRoot);
  const fixtureHash = computeFixtureHash(repositoryRoot);
  const status = git(['status', '--porcelain', '--untracked-files=all']).trim();
  const candidateWorktreeState = status ? 'DIRTY_CANDIDATE' : 'CLEAN_SHA';
  const archiveRoot = resolve(workRoot, 'head-source');
  const archiveTar = resolve(workRoot, 'head-source.tar');
  const beforeWebRoot = resolve(workRoot, 'before-web');
  const finalWebRoot = resolve(workRoot, 'final-web');
  const cleanup = { before: null, final: null };
  let baselineBuildHash = '0'.repeat(64);
  let candidateBuildHash = '0'.repeat(64);

  mkdirSync(workRoot, { recursive: true });
  try {
    mkdirSync(archiveRoot, { recursive: true });
    run('git', [
      'archive',
      '--format=tar',
      `--output=${archiveTar}`,
      'HEAD',
      '--',
      studioUiRelative,
      canonicalWwwrootRelative
    ], repositoryRoot);
    run('tar.exe', ['-xf', archiveTar, '-C', archiveRoot], repositoryRoot);
    const archivedStudioUi = resolve(archiveRoot, studioUiRelative);
    symlinkSync(resolve(studioUiRoot, 'node_modules'), resolve(archivedStudioUi, 'node_modules'), 'junction');

    buildStudioUi(archivedStudioUi, beforeWebRoot);
    buildStudioUi(studioUiRoot, finalWebRoot);
    baselineBuildHash = hashDirectory(resolve(beforeWebRoot, 'studio'));
    candidateBuildHash = hashDirectory(resolve(finalWebRoot, 'studio'));

    runPlaywright({ runId, phase: 'before', port: beforePort, webRoot: beforeWebRoot });
    cleanup.before = await verifyServerCleanup(beforePort);
    runPlaywright({ runId, phase: 'final', port: finalPort, webRoot: finalWebRoot });
    cleanup.final = await verifyServerCleanup(finalPort);

    const summary = aggregateFinalMatrix({
      runId,
      runRoot,
      headSha,
      candidateContentHash,
      candidateWorktreeState,
      fixtureHash,
      baselineBuildHash,
      candidateBuildHash,
      beforePort,
      finalPort,
      cleanup
    });
    if (summary.status !== 'PASS') {
      throw new Error(`R2 final matrix aggregation failed:\n${summary.errors.join('\n')}`);
    }
    return summary;
  } finally {
    rmSync(workRoot, { recursive: true, force: true });
  }
}

export function aggregateFinalMatrix(options) {
  const matrixRoot = resolve(options.runRoot, 'final-matrix');
  const errors = [];
  const groups = [];
  const actualGroups = existsSync(matrixRoot)
    ? readdirSync(matrixRoot, { withFileTypes: true }).filter(entry => entry.isDirectory()).map(entry => entry.name).sort()
    : [];
  for (const [name, value] of [
    ['baselineBuildHash', options.baselineBuildHash],
    ['candidateBuildHash', options.candidateBuildHash]
  ]) {
    if (!/^[0-9a-f]{64}$/.test(value ?? '') || /^0{64}$/.test(value)) {
      errors.push(`${name} must be a non-zero SHA-256 from a completed build.`);
    }
  }
  for (const expected of expectedGroups) {
    if (!actualGroups.includes(expected)) errors.push(`Missing matrix group: ${expected}.`);
  }
  for (const actual of actualGroups) {
    if (!expectedGroups.includes(actual)) errors.push(`Unexpected matrix group: ${actual}.`);
  }
  errors.push(...validateMatrixCleanup(options.cleanup, {
    before: options.beforePort,
    final: options.finalPort
  }));

  for (const groupId of expectedGroups.filter(group => actualGroups.includes(group))) {
    const groupRoot = resolve(matrixRoot, groupId);
    const requiredFiles = groupId.startsWith('S05-')
      ? [...phaseFiles, ...s05SourceFiles]
      : phaseFiles;
    const actualFiles = readdirSync(groupRoot, { withFileTypes: true })
      .filter(entry => entry.isFile())
      .map(entry => entry.name)
      .sort();
    const groupErrors = [];
    for (const required of requiredFiles) {
      if (!actualFiles.includes(required)) groupErrors.push(`missing ${required}`);
    }
    for (const actual of actualFiles) {
      if (![...requiredFiles, 'pair.json'].includes(actual)) groupErrors.push(`unexpected file ${actual}`);
    }
    if (groupErrors.length > 0) {
      errors.push(`${groupId}: ${groupErrors.join('; ')}.`);
      continue;
    }
    try {
      const pair = validateGroupPair(groupRoot, groupId);
      writeFileSync(resolve(groupRoot, 'pair.json'), `${JSON.stringify(pair, null, 2)}\n`, 'utf8');
      groups.push(pair);
      if (pair.comparability !== 'COMPARABLE') {
        errors.push(`${groupId}: ${pair.nonComparableReasons.join('; ')}.`);
      }
    } catch (error) {
      errors.push(`${groupId}: ${error instanceof Error ? error.message : String(error)}.`);
    }
  }

  const summary = {
    schemaVersion: 'r2-final-matrix-summary.v1',
    runId: options.runId,
    generatedAtUtc: new Date().toISOString(),
    status: errors.length === 0 && groups.length === 42 ? 'PASS' : 'FAIL',
    expectedGroupCount: 42,
    groupCount: groups.length,
    comparableGroupCount: groups.filter(group => group.comparability === 'COMPARABLE').length,
    baseline: {
      headSha: options.headSha,
      contentHash: createHash('sha256').update(`HEAD\0${options.headSha}`).digest('hex'),
      worktreeState: 'CLEAN_HEAD_STATIC',
      buildHash: options.baselineBuildHash
    },
    candidate: {
      headSha: options.headSha,
      contentHash: options.candidateContentHash,
      worktreeState: options.candidateWorktreeState,
      buildHash: options.candidateBuildHash
    },
    fixtureHash: options.fixtureHash,
    harness: {
      config: relative(repositoryRoot, matrixConfig).replaceAll('\\', '/'),
      testCount: 42,
      workers: 1,
      fullyParallel: false,
      hostKind: 'PLAYWRIGHT_CHROMIUM',
      ports: { before: options.beforePort, final: options.finalPort }
    },
    serverCleanup: options.cleanup,
    claim: {
      evidenceClass: 'REPOSITORY_PLAYWRIGHT',
      scope: 'CHROMIUM_DIRTY_CANDIDATE_COMPARISON',
      formalAcceptance: 'PARTIAL',
      blindReview: 'NOT_PERFORMED'
    },
    groups,
    errors
  };
  const schemaErrors = validateAgainstSchema(summary, summarySchema);
  if (schemaErrors.length > 0) {
    summary.status = 'FAIL';
    summary.errors.push(...schemaErrors.map(error => `summary schema: ${error}`));
  }
  mkdirSync(options.runRoot, { recursive: true });
  writeFileSync(resolve(options.runRoot, 'matrix-summary.json'), `${JSON.stringify(summary, null, 2)}\n`, 'utf8');
  return summary;
}

export function validateGroupPair(groupRoot, groupId) {
  const [scene, variant] = groupId.split('-');
  const before = readJson(resolve(groupRoot, 'capture-before.json'));
  const final = readJson(resolve(groupRoot, 'capture-final.json'));
  const domBefore = readJson(resolve(groupRoot, 'dom-before.json'));
  const domAfter = readJson(resolve(groupRoot, 'dom-after.json'));
  const interactionBefore = readJson(resolve(groupRoot, 'interaction-before.json'));
  const interactionFinal = readJson(resolve(groupRoot, 'interaction-final.json'));
  const sourceEvidence = {};
  for (const [phase, capture, dom, interaction] of [
    ['before', before, domBefore, interactionBefore],
    ['final', final, domAfter, interactionFinal]
  ]) {
    if (capture.schemaVersion !== 'r2-final-matrix-capture.v1') throw new Error(`${phase} schemaVersion is invalid`);
    if (capture.scene !== scene || capture.variant !== variant) throw new Error(`${phase} identity does not match ${groupId}`);
    if (canonical(capture.dom) !== canonical(dom)) throw new Error(`${phase} capture DOM does not match its DOM report`);
    for (const key of ['schemaVersion', 'scene', 'variant', 'route', 'state', 'role', 'flags', 'owner', 'requests', 'runtimeRequests', 'allowedWrites', 'runtime', 'motion', 'requiredCriticalActions', 'notes', 'bitmapEvidence']) {
      if (canonical(capture[key]) !== canonical(interaction[key])) throw new Error(`${phase} interaction.${key} is not bound to capture`);
    }
    validateMatrixCapture(capture, dom, phase);
    if (scene === 'S05') {
      sourceEvidence[phase] = validateS05SourceArtifact(groupRoot, capture, phase);
    }
    const hashRoute = new URL(dom.url).hash;
    if (hashRoute !== capture.route) throw new Error(`${phase} DOM route ${hashRoute} does not match ${capture.route}`);
  }

  const reasons = [];
  compare(reasons, 'route', before.route, final.route);
  compare(reasons, 'state', before.state, final.state);
  compare(reasons, 'role', before.role, final.role);
  compare(reasons, 'flags', before.flags, final.flags);
  compare(reasons, 'owner capability', before.owner?.capability, final.owner?.capability);
  compare(reasons, 'request endpoint set', [...new Set(before.requests)].sort(), [...new Set(final.requests)].sort());
  compare(reasons, 'write request sequence', writeRequests(before.requests), writeRequests(final.requests));
  compare(reasons, 'allowed write set', [...before.allowedWrites].sort(), [...final.allowedWrites].sort());
  compare(reasons, 'expected HTTP errors', before.runtime.expectedHttpErrors, final.runtime.expectedHttpErrors);
  compare(reasons, 'required critical actions', before.requiredCriticalActions, final.requiredCriticalActions);
  compare(reasons, 'viewport', domBefore.viewport, domAfter.viewport);
  compare(reasons, 'theme', domBefore.theme, domAfter.theme);
  compare(reasons, 'density', domBefore.density, domAfter.density);
  compare(reasons, 'reduced motion', domBefore.reducedMotion, domAfter.reducedMotion);
  compare(reasons, 'focus', domBefore.focus, domAfter.focus);
  compare(reasons, 'page scroll', domBefore.pageScroll, domAfter.pageScroll);
  if (scene === 'S05') {
    compare(reasons, 'bitmap fixture contract', before.bitmapEvidence?.capture?.contract, final.bitmapEvidence?.capture?.contract);
    compare(reasons, 'ROI geometry', before.bitmapEvidence?.capture?.roi, final.bitmapEvidence?.capture?.roi);
    compare(
      reasons,
      'preview artifact response snapshot',
      summarizeS05ResponseSnapshot(before.bitmapEvidence?.source?.response),
      summarizeS05ResponseSnapshot(final.bitmapEvidence?.source?.response)
    );
  }

  const expectedSize = variant === 'B0' ? { width: 1920, height: 1080 } : { width: 1366, height: 768 };
  const beforeImage = inspectPng(resolve(groupRoot, 'before.png'));
  const finalImage = inspectPng(resolve(groupRoot, 'after.png'));
  compare(reasons, 'before screenshot dimensions', beforeImage.size, expectedSize);
  compare(reasons, 'final screenshot dimensions', finalImage.size, expectedSize);
  const pair = {
    pairId: groupId,
    scene,
    variant,
    route: final.route,
    state: final.state,
    comparability: reasons.length === 0 ? 'COMPARABLE' : 'NON_COMPARABLE',
    nonComparableReasons: reasons,
    beforeSha256: beforeImage.sha256,
    finalSha256: finalImage.sha256,
    rawArtifactSha256: Object.fromEntries([
      ...phaseFiles,
      ...(scene === 'S05' ? s05SourceFiles : [])
    ].map(file => [
      file,
      createHash('sha256').update(readFileSync(resolve(groupRoot, file))).digest('hex')
    ]))
  };
  if (scene === 'S05') {
    pair.bitmapEvidence = {
      contract: final.bitmapEvidence.capture.contract,
      roi: final.bitmapEvidence.capture.roi,
      source: sourceEvidence,
      before: summarizeCanvasEvidence(before.bitmapEvidence),
      final: summarizeCanvasEvidence(final.bitmapEvidence)
    };
  }
  return pair;
}

export function validateMatrixCapture(capture, dom, phase = 'capture') {
  validateRuntime(capture, phase);
  validateDom(capture, dom, phase);
  if (capture.scene === 'S05') validateS05BitmapEvidence(capture, phase);
}

export function validateS05BitmapEvidence(capture, phase = 'capture') {
  const evidence = capture.bitmapEvidence;
  if (!evidence?.beforeAction || !evidence?.capture) {
    throw new Error(`${phase} S05 bitmap evidence is required`);
  }
  for (const [point, value] of [['beforeAction', evidence.beforeAction], ['capture', evidence.capture]]) {
    if (canonical(value.contract) !== canonical({
      schemaVersion: s05BitmapContract.schemaVersion,
      contentType: s05BitmapContract.contentType,
      sha256: s05BitmapContract.sha256,
      byteLength: s05BitmapContract.byteLength,
      width: s05BitmapContract.width,
      height: s05BitmapContract.height,
      channels: s05BitmapContract.channels,
      samples: s05BitmapContract.samples
    })) throw new Error(`${phase} S05 ${point} bitmap contract does not match the canonical fixture`);
    const canvas = value.canvas ?? {};
    if (canvas.readable !== true || canvas.error !== null) throw new Error(`${phase} S05 ${point} canvas pixels are unreadable`);
    if (!Number.isInteger(canvas.backing?.width) || canvas.backing.width < 1 ||
      !Number.isInteger(canvas.backing?.height) || canvas.backing.height < 1) {
      throw new Error(`${phase} S05 ${point} canvas backing dimensions are invalid`);
    }
    if (!(canvas.css?.width > 0) || !(canvas.css?.height > 0) ||
      (phase !== 'before' && canvas.inViewport !== true)) {
      throw new Error(`${phase} S05 ${point} canvas is not visibly framed in the viewport`);
    }
    const pixelCount = canvas.backing.width * canvas.backing.height;
    if (canvas.byteLength !== pixelCount * s05BitmapContract.channels ||
      !Number.isInteger(canvas.nonTransparentPixels) || canvas.nonTransparentPixels < 1) {
      throw new Error(`${phase} S05 ${point} canvas is blank or has an invalid pixel buffer`);
    }
    const requiresMulticolor = phase !== 'before' || point === 'beforeAction';
    if (!Number.isInteger(canvas.uniqueColorCount) || canvas.uniqueColorCount < (requiresMulticolor ? 2 : 1)) {
      throw new Error(`${phase} S05 ${point} canvas does not contain a multicolor bitmap`);
    }
    if (!/^[0-9a-f]{64}$/.test(canvas.sha256 ?? '') || /^0{64}$/.test(canvas.sha256)) {
      throw new Error(`${phase} S05 ${point} canvas SHA-256 is invalid`);
    }
    const legacyEmptyStaleCapture = phase === 'before' && point === 'capture' &&
      capture.variant === 'EXCEPTION';
    if (!legacyEmptyStaleCapture) {
      validateS05CanvasSourceSamples(
        canvas,
        value.contract.samples,
        phase,
        point,
        phase !== 'before'
      );
    }
  }
  const beforeRoi = validateS05Roi(evidence.beforeAction.roi, phase, 'beforeAction');
  const roi = validateS05Roi(evidence.capture.roi, phase, 'capture');
  const canonicalRoi = canonical(s05BitmapContract.roi);
  if (beforeRoi.phase !== 'ready' || canonical(pickRoiGeometry(beforeRoi)) !== canonicalRoi) {
    throw new Error(`${phase} S05 beforeAction ROI must be the ready canonical fixture`);
  }
  if (capture.variant === 'EXCEPTION') {
    if (roi.phase !== 'stale' || canonical(pickRoiGeometry(roi)) === canonicalRoi) {
      throw new Error(`${phase} S05 EXCEPTION must capture changed stale ROI geometry`);
    }
  } else if (canonical(pickRoiGeometry(roi)) !== canonicalRoi) {
    throw new Error(`${phase} S05 ROI geometry does not match the canonical fixture`);
  }
  if (capture.variant === 'B2') {
    if (roi.phase !== 'editing') throw new Error(`${phase} S05 B2 must capture the ROI editing phase`);
    if (evidence.beforeAction.canvas.sha256 === evidence.capture.canvas.sha256) {
      throw new Error(`${phase} S05 B2 ROI overlay did not change the canvas pixels`);
    }
  }
}

function validateS05CanvasSourceSamples(canvas, expectedSamples, phase, point, requirePixelMatch) {
  if (!Array.isArray(expectedSamples) || expectedSamples.length < 3 ||
    !Array.isArray(canvas.sourceSamples) || canvas.sourceSamples.length !== expectedSamples.length) {
    throw new Error(`${phase} S05 ${point} canvas source samples are incomplete`);
  }
  for (const [index, expected] of expectedSamples.entries()) {
    const sample = canvas.sourceSamples[index];
    if (canonical({ x: sample?.x, y: sample?.y, expected: sample?.expected }) !== canonical({
      x: expected.x,
      y: expected.y,
      expected: expected.rgba
    })) {
      throw new Error(`${phase} S05 ${point} canvas source sample identity is invalid`);
    }
    if (!Array.isArray(sample.observed) || sample.observed.length !== s05BitmapContract.channels ||
      !sample.observed.every(channel => Number.isInteger(channel) && channel >= 0 && channel <= 255) ||
      !Number.isInteger(sample.backingX) || sample.backingX < 0 || sample.backingX >= canvas.backing.width ||
      !Number.isInteger(sample.backingY) || sample.backingY < 0 || sample.backingY >= canvas.backing.height ||
      !Number.isFinite(sample.maxChannelDelta) || sample.maxChannelDelta < 0 ||
      (requirePixelMatch && sample.maxChannelDelta > 8)) {
      throw new Error(`${phase} S05 ${point} canvas pixels do not match the canonical source samples`);
    }
  }
}

export function validateS05SourceArtifact(groupRoot, capture, phase) {
  const expectedPath = phase === 'before' ? 'source-before.png' : 'source-final.png';
  const source = capture.bitmapEvidence?.source;
  if (!source || source.path !== expectedPath) {
    throw new Error(`${phase} S05 source artifact path is invalid`);
  }
  const sourcePath = resolve(groupRoot, expectedPath);
  const image = inspectPng(sourcePath);
  const byteLength = statSync(sourcePath).size;
  if (source.sha256 !== image.sha256 || source.sha256 !== s05BitmapContract.sha256) {
    throw new Error(`${phase} S05 source artifact SHA-256 does not match the canonical fixture`);
  }
  if (source.byteLength !== byteLength || byteLength !== s05BitmapContract.byteLength) {
    throw new Error(`${phase} S05 source artifact length does not match the canonical fixture`);
  }
  if (canonical(image.size) !== canonical({
    width: s05BitmapContract.width,
    height: s05BitmapContract.height
  })) {
    throw new Error(`${phase} S05 source artifact dimensions do not match the canonical fixture`);
  }
  if (source.contentType !== s05BitmapContract.contentType || source.status !== 200) {
    throw new Error(`${phase} S05 source artifact HTTP metadata is invalid`);
  }
  validateS05SourceBinding(capture, source, phase);
  return Object.freeze({
    path: expectedPath,
    sha256: image.sha256,
    byteLength,
    contentType: source.contentType,
    status: source.status,
    response: source.response,
    size: image.size
  });
}

export function validateS05SourceBinding(capture, source, phase = 'capture') {
  const response = source?.response;
  if (!response || response.source !== 'PLAYWRIGHT_ROUTE_FULFILL_RESPONSE_SNAPSHOT' ||
    response.method !== 'GET' ||
    !/^\/api\/preview-artifacts\/[A-Za-z0-9_-]{43}$/.test(response.path ?? '') ||
    response.status !== 200 || response.contentType !== s05BitmapContract.contentType ||
    response.sha256 !== source.sha256 || response.byteLength !== source.byteLength) {
    throw new Error(`${phase} S05 source artifact is not bound to a valid route fulfill response snapshot`);
  }
  let responsePath;
  try {
    responsePath = new URL(response.url).pathname;
  } catch {
    throw new Error(`${phase} S05 route fulfill response snapshot URL is invalid`);
  }
  if (responsePath !== response.path) {
    throw new Error(`${phase} S05 route fulfill response snapshot URL and path do not match`);
  }
  if (!Array.isArray(capture.runtimeRequests)) {
    throw new Error(`${phase} S05 runtimeRequests must be a structured array`);
  }
  const matches = capture.runtimeRequests.filter(request =>
    request?.method === response.method && request.path === response.path && request.url === response.url);
  if (matches.length !== 1) {
    throw new Error(`${phase} S05 source artifact must bind to exactly one matching runtime GET; found ${matches.length}`);
  }
}

function summarizeS05ResponseSnapshot(response) {
  if (!response) return null;
  return {
    source: response.source,
    method: response.method,
    path: response.path,
    status: response.status,
    contentType: response.contentType,
    sha256: response.sha256,
    byteLength: response.byteLength
  };
}

function validateS05Roi(roi, phase, point) {
  if (roi?.kind !== 'rectangle' || !['ready', 'editing', 'stale'].includes(roi.phase)) {
    throw new Error(`${phase} S05 ${point} ROI identity or phase is invalid`);
  }
  for (const name of ['x', 'y', 'width', 'height']) {
    if (!Number.isFinite(roi[name])) throw new Error(`${phase} S05 ${point} ROI ${name} is invalid`);
  }
  if (roi.x < 0 || roi.y < 0 || roi.width <= 0 || roi.height <= 0 ||
    roi.x + roi.width > s05BitmapContract.width || roi.y + roi.height > s05BitmapContract.height) {
    throw new Error(`${phase} S05 ${point} ROI geometry is outside the canonical bitmap`);
  }
  return roi;
}

function pickRoiGeometry(roi) {
  return { x: roi.x, y: roi.y, width: roi.width, height: roi.height };
}

function summarizeCanvasEvidence(evidence) {
  return {
    beforeActionSha256: evidence.beforeAction.canvas.sha256,
    captureSha256: evidence.capture.canvas.sha256,
    overlayChanged: evidence.beforeAction.canvas.sha256 !== evidence.capture.canvas.sha256,
    backing: evidence.capture.canvas.backing,
    css: evidence.capture.canvas.css,
    nonTransparentPixels: evidence.capture.canvas.nonTransparentPixels,
    uniqueColorCount: evidence.capture.canvas.uniqueColorCount,
    sourceSamples: evidence.capture.canvas.sourceSamples
  };
}

function validateRuntime(capture, phase) {
  if (!Array.isArray(capture.requests)) throw new Error(`${phase} requests must be an array`);
  if (!Array.isArray(capture.runtimeRequests)) throw new Error(`${phase} runtimeRequests must be an array`);
  for (const request of capture.runtimeRequests) {
    if (!request || !/^[A-Z]+$/.test(request.method ?? '') ||
      typeof request.path !== 'string' || !request.path.startsWith('/') ||
      typeof request.url !== 'string') {
      throw new Error(`${phase} runtimeRequests contains invalid request metadata`);
    }
    let path;
    try {
      path = new URL(request.url).pathname;
    } catch {
      throw new Error(`${phase} runtimeRequests contains an invalid URL`);
    }
    if (path !== request.path) throw new Error(`${phase} runtimeRequests URL and path do not match`);
  }
  if (!Array.isArray(capture.allowedWrites)) throw new Error(`${phase} allowedWrites must be an array`);
  const runtime = capture.runtime ?? {};
  for (const name of ['consoleErrors', 'pageErrors', 'failedRequests', 'httpErrors', 'expectedHttpErrors', 'observedExpectedHttpErrors', 'unexpectedWrites']) {
    if (!Array.isArray(runtime[name])) throw new Error(`${phase} runtime.${name} must be an array`);
  }
  for (const name of ['consoleErrors', 'pageErrors', 'failedRequests', 'httpErrors', 'unexpectedWrites']) {
    if (runtime[name].length > 0) throw new Error(`${phase} runtime.${name} must be empty`);
  }
  if (canonical([...runtime.expectedHttpErrors].sort()) !== canonical([...runtime.observedExpectedHttpErrors].sort())) {
    throw new Error(`${phase} expected HTTP errors were not observed exactly`);
  }
  if (new Set(capture.allowedWrites).size !== capture.allowedWrites.length) {
    throw new Error(`${phase} allowedWrites must be unique`);
  }
  const approvedWrites = new Set(approvedWritesByScene[capture.scene] ?? []);
  const invalidAllowances = capture.allowedWrites.filter(write => !approvedWrites.has(write));
  if (invalidAllowances.length > 0) {
    throw new Error(`${phase} has unapproved write allowances: ${invalidAllowances.join(', ')}`);
  }
  const writes = writeRequests(capture.requests);
  const unusedAllowances = capture.allowedWrites.filter(write => !writes.includes(write));
  if (unusedAllowances.length > 0) {
    throw new Error(`${phase} has unused write allowances: ${unusedAllowances.join(', ')}`);
  }
  const unexpectedWrites = writes.filter(write => !capture.allowedWrites.includes(write));
  if (unexpectedWrites.length > 0) {
    throw new Error(`${phase} has unexpected writes: ${unexpectedWrites.join(', ')}`);
  }
  if (capture.owner?.mounted !== 1) throw new Error(`${phase} owner must be mounted exactly once`);
  if (!capture.owner?.capability) throw new Error(`${phase} owner capability is missing`);
}

export function validateMatrixCleanup(cleanup, ports) {
  const errors = [];
  for (const phase of ['before', 'final']) {
    const evidence = cleanup?.[phase];
    if (!evidence || typeof evidence !== 'object') {
      errors.push(`serverCleanup.${phase} is required.`);
      continue;
    }
    if (evidence.port !== ports[phase]) {
      errors.push(`serverCleanup.${phase}.port must equal ${ports[phase]}.`);
    }
    if (evidence.endpointUnreachable !== true) {
      errors.push(`serverCleanup.${phase}.endpointUnreachable must be true.`);
    }
    if (evidence.listenerReleased !== true) {
      errors.push(`serverCleanup.${phase}.listenerReleased must be true.`);
    }
  }
  return errors;
}

function validateDom(capture, dom, phase) {
  if (Number(dom.horizontalOverflow ?? 0) > 0) throw new Error(`${phase} has horizontal overflow`);
  if (!Array.isArray(capture.requiredCriticalActions) || capture.requiredCriticalActions.length === 0) {
    throw new Error(`${phase} required critical actions are missing`);
  }
  if (new Set(capture.requiredCriticalActions).size !== capture.requiredCriticalActions.length) {
    throw new Error(`${phase} required critical actions must be unique`);
  }
  for (const selector of capture.requiredCriticalActions) {
    const matches = (dom.criticalActions ?? []).filter(action => action.selector === selector);
    if (matches.length !== 1) throw new Error(`${phase} critical action ${selector} count is ${matches.length}`);
    const [action] = matches;
    if (action.truncated || !action.inViewport || !action.reachable || !action.enabled || !action.unobscured) {
      throw new Error(`${phase} critical action ${selector} is not operable`);
    }
  }
}

function compare(reasons, label, before, final) {
  if (canonical(before) !== canonical(final)) reasons.push(`${label} differs between before and final`);
}

function writeRequests(requests) {
  return requests.filter(request => !/^(?:GET|HEAD) /.test(request));
}

function canonical(value) {
  if (Array.isArray(value)) return `[${value.map(canonical).join(',')}]`;
  if (value && typeof value === 'object') {
    return `{${Object.keys(value).sort().map(key => `${JSON.stringify(key)}:${canonical(value[key])}`).join(',')}}`;
  }
  return JSON.stringify(value);
}

function inspectPng(path) {
  const buffer = readFileSync(path);
  let size;
  try {
    size = inspectPngBuffer(buffer);
  } catch (error) {
    throw new Error(`${path} is not a decodable PNG: ${error instanceof Error ? error.message : String(error)}`);
  }
  return {
    sha256: createHash('sha256').update(buffer).digest('hex'),
    size
  };
}

function buildStudioUi(sourceRoot, webRoot) {
  mkdirSync(resolve(webRoot, 'studio'), { recursive: true });
  const executable = process.platform === 'win32' ? (process.env.ComSpec || 'cmd.exe') : 'npm';
  const args = process.platform === 'win32' ? ['/d', '/s', '/c', 'npm.cmd run build'] : ['run', 'build'];
  run(executable, args, sourceRoot, {
    VITE_OUT_DIR: resolve(webRoot, 'studio'),
    CONFIGURATION: 'Debug',
    TARGET_FRAMEWORK: 'net8.0-windows'
  });
}

function runPlaywright({ runId, phase, port, webRoot }) {
  run(process.execPath, [playwrightCli, 'test', '--config', matrixConfig], uiTestsRoot, {
    CV_UI_HOST: '127.0.0.1',
    CV_UI_PORT: String(port),
    CV_UI_WEB_ROOT: webRoot,
    CV_STUDIO_UI_EVIDENCE_PHASE: 'r2',
    CV_R2_CAPTURE_PHASE: phase,
    CV_R2_MATRIX_RUN_ID: runId
  });
}

function run(executable, args, cwd, extraEnv = {}) {
  const result = spawnSync(executable, args, {
    cwd,
    env: { ...process.env, ...extraEnv },
    stdio: 'inherit',
    windowsHide: true
  });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`${executable} exited with code ${result.status ?? 'unknown'}.`);
}

function git(args) {
  return execFileSync('git', args, { cwd: repositoryRoot, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
}

function hashDirectory(root) {
  const files = [];
  walk(root, files);
  files.sort((left, right) => left.localeCompare(right, 'en'));
  const hash = createHash('sha256');
  for (const file of files) {
    const relativePath = relative(root, file).replaceAll('\\', '/');
    hash.update(`${relativePath}\0`);
    hash.update(readFileSync(file));
  }
  return hash.digest('hex');
}

function walk(root, files) {
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const path = resolve(root, entry.name);
    if (entry.isDirectory()) walk(path, files);
    else if (entry.isFile()) files.push(path);
  }
}

async function verifyServerCleanup(port) {
  await assertPortReleased(port, 'teardown');
  return { port, endpointUnreachable: true, listenerReleased: true };
}

async function assertPortReleased(port, label) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    if (!(await canConnect(port))) return;
    await delay(100);
  }
  throw new Error(`R2 ${label} port ${port} remains reachable.`);
}

function canConnect(port) {
  return new Promise(resolvePromise => {
    const socket = connect({ host: '127.0.0.1', port });
    const done = value => {
      socket.destroy();
      resolvePromise(value);
    };
    socket.setTimeout(250, () => done(false));
    socket.once('connect', () => done(true));
    socket.once('error', () => done(false));
  });
}

function delay(milliseconds) {
  return new Promise(resolvePromise => setTimeout(resolvePromise, milliseconds));
}

async function runCli() {
  const [command = 'run', value] = process.argv.slice(2);
  if (command === 'run') {
    const summary = await runFinalMatrix({ runId: value });
    console.log(`R2 final matrix ${summary.status}: ${summary.comparableGroupCount}/42 comparable groups.`);
    return;
  }
  if (command === 'aggregate') {
    throw new Error('Standalone aggregate is disabled because it cannot prove build identity; run the complete dual matrix instead.');
  }
  throw new Error(`Unknown R2 final matrix command: ${command}.`);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  runCli().catch(error => {
    console.error(error instanceof Error ? error.stack ?? error.message : String(error));
    process.exitCode = 1;
  });
}
