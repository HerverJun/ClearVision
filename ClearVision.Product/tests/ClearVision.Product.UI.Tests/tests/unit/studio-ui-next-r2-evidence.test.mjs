import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { deflateSync } from 'node:zlib';
import {
  computeCandidateContentHash,
  computeFixtureHash,
  computeMotionSourceSnapshot,
  r2FixtureFiles,
  validateAgainstSchema,
  validateBlindReview,
  validateBrowserSession,
  validateEvidenceManifest,
  validateMotionInventory
} from '../e2e/studio-ui-next/r2-visual/validate-r2-evidence.mjs';
import {
  aggregateFinalMatrix,
  validateMatrixCapture,
  validateMatrixCleanup,
  validateS05BitmapEvidence,
  validateS05SourceBinding
} from '../e2e/studio-ui-next/r2-visual/r2-final-matrix-runner.mjs';
import { inspectPngBuffer } from '../e2e/studio-ui-next/r2-visual/r2-png.mjs';
import {
  validateIndependentNoNodeEvidence,
  validateNative125Evidence
} from '../e2e/studio-ui-next/r2-visual/validate-r2-external-evidence.mjs';
import {
  getSessionPaths,
  startSession,
  statusSession,
  stopSession
} from '../e2e/studio-ui-next/r2-visual/r2-browser-fixture-session.mjs';

const repositoryRoot = resolve(process.cwd(), '..', '..', '..');
const schemaRoot = resolve(
  process.cwd(),
  'tests/e2e/studio-ui-next/r2-visual'
);

function pngChunk(type, data) {
  const typeBuffer = Buffer.from(type, 'ascii');
  const output = Buffer.alloc(data.length + 12);
  output.writeUInt32BE(data.length, 0);
  typeBuffer.copy(output, 4);
  data.copy(output, 8);
  output.writeUInt32BE(crc32(Buffer.concat([typeBuffer, data])), data.length + 8);
  return output;
}

function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit += 1) {
      crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
    }
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function createPng(width, height, options = {}) {
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = options.bitDepth ?? 8;
  ihdr[9] = options.colorType ?? 6;
  const rowBytes = width * 4;
  const scanlines = options.scanlines ?? Buffer.alloc((rowBytes + 1) * height);
  if (!options.scanlines) {
    for (let row = 0; row < height; row += 1) {
      const rowStart = row * (rowBytes + 1);
      scanlines[rowStart] = options.filter ?? 0;
      for (let column = 0; column < width; column += 1) {
        const pixel = rowStart + 1 + column * 4;
        scanlines[pixel] = 32;
        scanlines[pixel + 1] = 96;
        scanlines[pixel + 2] = 160;
        scanlines[pixel + 3] = 255;
      }
    }
  }
  const chunks = [pngChunk('IHDR', ihdr)];
  if (options.palette) chunks.push(pngChunk('PLTE', options.palette));
  chunks.push(pngChunk('IDAT', options.idat ?? deflateSync(scanlines)));
  chunks.push(pngChunk('IEND', Buffer.alloc(0)));
  return Buffer.concat([Buffer.from('89504e470d0a1a0a', 'hex'), ...chunks]);
}

function createEvidenceFixture(candidateRepositoryRoot = repositoryRoot) {
  const root = mkdtempSync(join(tmpdir(), 'clearvision-r2-evidence-'));
  const before = createPng(1920, 1080);
  const after = createPng(1920, 1080);
  writeFileSync(join(root, 'before.png'), before);
  writeFileSync(join(root, 'after.png'), after);
  const hash = buffer => createHash('sha256').update(buffer).digest('hex');
  const runtime = {
    requests: ['GET /api/feature-flags'],
    allowedWrites: [],
    consoleErrors: [],
    pageErrors: [],
    failedRequests: [],
    httpErrors: [],
    expectedHttpErrors: [],
    observedExpectedHttpErrors: [],
    unexpectedWrites: []
  };
  const criticalActions = [{
    selector: '[data-design-theme="dark"]',
    truncated: false,
    inViewport: true,
    reachable: true,
    enabled: true,
    unobscured: true
  }];
  const identity = {
    scene: 'S01',
    pairId: 'r2-design-main-b0-light-compact',
    route: '#/labs/design',
    state: 'main'
  };
  const reports = {
    'dom-before.json': { ...identity, criticalActions },
    'dom-after.json': { ...identity, criticalActions },
    'interaction.json': {
      ...identity,
      runtime,
      requiredCriticalActions: ['[data-design-theme="dark"]']
    }
  };
  const reportArtifacts = {};
  for (const [name, report] of Object.entries(reports)) {
    const buffer = Buffer.from(`${JSON.stringify(report, null, 2)}\n`, 'utf8');
    writeFileSync(join(root, name), buffer);
    reportArtifacts[name] = { path: name, sha256: hash(buffer) };
  }
  const document = {
    schemaVersion: 'r2-evidence.v1',
    headSha: git(candidateRepositoryRoot, ['rev-parse', 'HEAD']).trim().toLowerCase(),
    candidateContentHash: computeCandidateContentHash(candidateRepositoryRoot),
    worktreeState: 'DIRTY_CANDIDATE',
    stage: 'R2.1',
    batch: 'design-language',
    scene: identity.scene,
    route: '#/labs/design',
    state: 'main',
    fixture: { id: 'r2-design-main', hash: computeFixtureHash(candidateRepositoryRoot) },
    role: 'Public',
    profile: 'NEXT_DEFAULT',
    flags: {},
    evidenceClass: 'IN_APP_BROWSER_ITERATION',
    hostKind: 'IN_APP_BROWSER',
    browserSurface: 'codex-in-app-browser',
    baseUrl: 'http://127.0.0.1:5177/studio/index.html',
    server: { owner: 'R2_BROWSER_FIXTURE_SESSION', pid: 123, port: 5177, createdByBatch: true, cleanupToken: 'a'.repeat(32) },
    viewport: { width: 1920, height: 1080 },
    window: 'NOT_PERFORMED',
    client: 'NOT_PERFORMED',
    dpr: 1,
    nativeDpi: 'NOT_PERFORMED',
    theme: 'light',
    density: 'compact',
    reducedMotion: false,
    motionProfile: 'normal',
    motionClock: 'settled',
    focus: 'body',
    scrollOwner: 'page',
    runtime,
    ownerLedger: [{ capability: 'design-lab', mounted: 1, subscriptions: 0, writes: 0 }],
    requiredCriticalActions: ['[data-design-theme="dark"]'],
    metrics: {
      domBoxes: {},
      computedStyles: {},
      criticalActions,
      horizontalOverflow: 0,
      unrecordedNestedScrollOwners: 0,
      criticalActionTruncationCount: 0,
      criticalActionUnreachableCount: 0,
      layoutShift: 0
    },
    screenshots: {
      before: { path: 'before.png', sha256: hash(before), width: 1920, height: 1080 },
      after: { path: 'after.png', sha256: hash(after), width: 1920, height: 1080 }
    },
    reports: {
      domBefore: reportArtifacts['dom-before.json'],
      domAfter: reportArtifacts['dom-after.json'],
      interaction: reportArtifacts['interaction.json']
    },
    iteration: 1,
    cleanup: {
      status: 'PASS',
      timers: 0,
      animationFrames: 0,
      listeners: 0,
      requests: 0,
      serverStopped: true,
      ownershipMarkerVerified: true,
      endpointUnreachable: true,
      pidExited: true
    },
    pairId: identity.pairId,
    comparability: 'COMPARABLE',
    nonComparableReasons: [],
    claimScope: 'DIRECTIONAL_BROWSER'
  };
  return { root, document };
}

function createMatrixCapture() {
  return {
    scene: 'S01',
    requests: ['GET /api/feature-flags'],
    runtimeRequests: [{
      method: 'GET',
      path: '/api/feature-flags',
      url: 'http://127.0.0.1:5318/api/feature-flags'
    }],
    allowedWrites: [],
    runtime: {
      consoleErrors: [],
      pageErrors: [],
      failedRequests: [],
      httpErrors: [],
      expectedHttpErrors: [],
      observedExpectedHttpErrors: [],
      unexpectedWrites: []
    },
    owner: { capability: 'overview', mounted: 1 },
    requiredCriticalActions: ['[data-r2-action="open"]']
  };
}

function createMatrixDom() {
  return {
    horizontalOverflow: 0,
    criticalActions: [{
      selector: '[data-r2-action="open"]',
      truncated: false,
      inViewport: true,
      reachable: true,
      enabled: true,
      unobscured: true
    }]
  };
}

function createS05BitmapCapture(variant = 'B2') {
  const contract = {
    schemaVersion: 'f03-preview-bitmap.v1',
    contentType: 'image/png',
    sha256: '743aefc2299260923475866134e9218560b182d92bfc282660f9c0e4f18ea815',
    byteLength: 2384,
    width: 100,
    height: 100,
    channels: 4,
    samples: [
      { x: 5, y: 85, rgba: [24, 31, 38, 255] },
      { x: 95, y: 85, rgba: [24, 31, 38, 255] },
      { x: 5, y: 60, rgba: [47, 55, 62, 255] },
      { x: 95, y: 60, rgba: [47, 55, 62, 255] },
      { x: 58, y: 57, rgba: [204, 208, 208, 255] },
      { x: 45, y: 57, rgba: [165, 171, 174, 255] },
      { x: 70, y: 57, rgba: [168, 174, 177, 255] },
      { x: 70, y: 70, rgba: [70, 79, 86, 255] },
      { x: 58, y: 82, rgba: [158, 165, 170, 255] }
    ]
  };
  const canvas = sha256 => ({
    readable: true,
    error: null,
    backing: { width: 640, height: 480 },
    css: { x: 10, y: 10, width: 640, height: 480 },
    inViewport: true,
    byteLength: 640 * 480 * 4,
    nonTransparentPixels: 640 * 480,
    uniqueColorCount: 12,
    sha256,
    sourceSamples: contract.samples.map((sample, index) => ({
      x: sample.x,
      y: sample.y,
      expected: [...sample.rgba],
      observed: [...sample.rgba],
      backingX: 100 + index,
      backingY: 100 + index,
      maxChannelDelta: 0
    }))
  });
  const ready = { phase: 'ready', kind: 'rectangle', x: 10, y: 10, width: 30, height: 20 };
  return {
    scene: 'S05',
    variant,
    bitmapEvidence: {
      beforeAction: { contract, canvas: canvas('1'.repeat(64)), roi: ready },
      capture: {
        contract,
        canvas: canvas(variant === 'B2' ? '2'.repeat(64) : '1'.repeat(64)),
        roi: variant === 'B2'
          ? { ...ready, phase: 'editing' }
          : variant === 'EXCEPTION'
            ? { ...ready, phase: 'stale', x: 14 }
            : ready
      }
    }
  };
}

function replaceReport(fixture, name, mutate) {
  const artifact = fixture.document.reports[name];
  const path = join(fixture.root, artifact.path);
  const report = JSON.parse(readFileSync(path, 'utf8'));
  mutate(report);
  const buffer = Buffer.from(`${JSON.stringify(report, null, 2)}\n`, 'utf8');
  writeFileSync(path, buffer);
  artifact.sha256 = createHash('sha256').update(buffer).digest('hex');
}

function reviewScores(score = 4) {
  return {
    visual_focus: score,
    material_hierarchy: score,
    operation_priority: score,
    cross_page_consistency: score
  };
}

function reviewReasons() {
  return {
    visual_focus: '当前对象和主操作在首屏形成稳定视线顺序',
    material_hierarchy: '主舞台和辅助表面通过明度与空间清楚区分',
    operation_priority: '唯一主操作固定且危险动作保持独立语义',
    cross_page_consistency: '连续页面共享对象上下文和相同动作位置'
  };
}

function createReview(groupCount = 4, reviewers = ['reviewer-a', 'reviewer-b']) {
  const variants = ['B0', 'B2', 'EXCEPTION'];
  return {
    schemaVersion: 'r2-review.v1',
    reviewId: 'r2-local-review',
    scope: 'LOCAL_BATCH',
    seed: 42,
    finalSide: 'right',
    reviewers,
    groups: Array.from({ length: groupCount }, (_, index) => ({
      pairId: `pair-${index}`,
      scene: `S${String(index % 14).padStart(2, '0')}`,
      variant: variants[index % variants.length],
      routes: ['#/login', '#/projects'],
      comparability: 'COMPARABLE',
      votes: reviewers.map(reviewer => ({ reviewer, preference: 'right', scores: reviewScores(), reasons: reviewReasons() }))
    })),
    disagreement: []
  };
}

function createFinalReview(passedGroupCount = 42) {
  const reviewers = ['reviewer-a', 'reviewer-b', 'reviewer-c'];
  const variants = ['B0', 'B2', 'EXCEPTION'];
  const document = createReview(42, reviewers);
  document.scope = 'FINAL';
  document.reviewId = 'r2-final-review';
  document.groups = Array.from({ length: 14 }, (_, sceneIndex) => variants.map((variant, variantIndex) => {
    const groupIndex = sceneIndex * 3 + variantIndex;
    const passed = groupIndex < passedGroupCount;
    return {
      pairId: `S${String(sceneIndex).padStart(2, '0')}-${variant}`,
      scene: `S${String(sceneIndex).padStart(2, '0')}`,
      variant,
      routes: ['#/login', '#/projects'],
      comparability: 'COMPARABLE',
      votes: reviewers.map(reviewer => ({
        reviewer,
        preference: passed ? 'right' : 'left',
        scores: reviewScores(),
        reasons: reviewReasons()
      }))
    };
  })).flat();
  return document;
}

function createMotion(overrides = {}) {
  return {
    schemaVersion: 'r2-motion.v1',
    generatedFrom: '1'.repeat(40),
    sourceHash: '2'.repeat(64),
    sourceFiles: ['design-system/primitives/CvMenu.vue'],
    items: [{
      motionId: 'cv-menu-enter',
      purpose: 'SPATIAL_CONTINUITY',
      owner: 'CvMenu',
      trigger: 'open',
      target: '.cv-menu__content',
      mechanism: 'CSS_TRANSITION',
      properties: ['opacity', 'transform'],
      durationMs: 140,
      delayMs: 0,
      easing: 'var(--cv-motion-ease-standard)',
      stableKey: 'menu-id',
      cancellation: 'close or route leave',
      dispose: 'unmount removes outside listeners',
      reducedMotion: { durationMs: 1, delayMs: 0, transformDistancePx: 0, infiniteAnimations: 0 },
      focusAria: 'focus returns to trigger and aria-expanded updates immediately',
      risk: 'LOW',
      sourceRefs: ['design-system/primitives/CvMenu.vue'],
      ...overrides
    }]
  };
}

test('R2 schemas are valid UTF-8 JSON contracts with required fields', () => {
  for (const name of ['r2-evidence.schema.json', 'r2-review.schema.json', 'r2-motion.schema.json', 'r2-in-app-browser.schema.json']) {
    const schema = JSON.parse(readFileSync(join(schemaRoot, name), 'utf8'));
    assert.equal(schema.type, 'object');
    assert.ok(schema.$id.startsWith('clearvision://'));
    assert.ok(schema.required.length > 0);
    assert.deepEqual(validateAgainstSchema({}, schema).some(error => error.includes('required')), true);
  }
});

test('R2 PNG validator accepts a complete decodable RGBA image', () => {
  assert.deepEqual(inspectPngBuffer(createPng(7, 5)), { width: 7, height: 5 });
});

test('R2 PNG validator rejects truncated, CRC-corrupt, and header-only files', () => {
  const complete = createPng(7, 5);
  const crcCorrupt = Buffer.from(complete);
  crcCorrupt[16] ^= 0x01;
  const headerOnly = Buffer.alloc(24);
  Buffer.from('89504e470d0a1a0a', 'hex').copy(headerOnly, 0);
  headerOnly.write('IHDR', 12, 'ascii');

  assert.throws(() => inspectPngBuffer(complete.subarray(0, complete.length - 2)), /truncated/);
  assert.throws(() => inspectPngBuffer(crcCorrupt), /CRC is invalid/);
  assert.throws(() => inspectPngBuffer(headerOnly), /minimum structure is invalid/);
});

test('R2 PNG validator rejects corrupt image data, scanline drift, and invalid filters', () => {
  assert.throws(
    () => inspectPngBuffer(createPng(2, 2, { idat: Buffer.from([0x00, 0x01, 0x02]) })),
    /cannot be decompressed/
  );
  assert.throws(
    () => inspectPngBuffer(createPng(2, 2, { scanlines: Buffer.alloc(17) })),
    /scanline length is invalid/
  );
  const invalidFilter = Buffer.alloc(18);
  invalidFilter[0] = 5;
  assert.throws(
    () => inspectPngBuffer(createPng(2, 2, { scanlines: invalidFilter })),
    /invalid filter type/
  );
});

test('R2 PNG validator rejects indexed images without a palette', () => {
  assert.throws(() => inspectPngBuffer(createPng(2, 2, { colorType: 3 })), /missing PLTE/);
});

test('R2 evidence accepts a complete directional browser manifest', t => {
  const fixture = createEvidenceFixture();
  t.after(() => rmSync(fixture.root, { recursive: true, force: true }));
  assert.deepEqual(validateEvidenceManifest(fixture.document, { repositoryRoot, manifestPath: join(fixture.root, 'manifest.json') }), []);
});

test('R2 formal evidence accepts a clean SHA-bound repository candidate', t => {
  const cleanRepository = createCleanGitRepository();
  const fixture = createEvidenceFixture(cleanRepository);
  t.after(() => {
    rmSync(fixture.root, { recursive: true, force: true });
    rmSync(cleanRepository, { recursive: true, force: true });
  });
  fixture.document.worktreeState = 'CLEAN_SHA';
  fixture.document.stage = 'R2.7';
  fixture.document.evidenceClass = 'REPOSITORY_PLAYWRIGHT';
  fixture.document.hostKind = 'PLAYWRIGHT_CHROMIUM';
  fixture.document.browserSurface = 'repository-playwright';
  fixture.document.claimScope = 'FORMAL_CHROMIUM';
  assert.deepEqual(validateEvidenceManifest(fixture.document, {
    repositoryRoot: cleanRepository,
    manifestPath: join(fixture.root, 'manifest.json'),
    formal: true
  }), []);
});

test('R2 formal evidence rejects dirty candidates and any worktree residue', t => {
  const fixture = createEvidenceFixture();
  t.after(() => rmSync(fixture.root, { recursive: true, force: true }));
  let errors = validateEvidenceManifest(fixture.document, {
    repositoryRoot,
    manifestPath: join(fixture.root, 'manifest.json'),
    formal: true
  });
  assert.ok(errors.some(error => error.includes('worktreeState=CLEAN_SHA')));
  assert.ok(errors.some(error => error.includes('stage R2.7')));
  assert.ok(errors.some(error => error.includes('Repository Playwright')));

  const cleanRepository = createCleanGitRepository();
  t.after(() => rmSync(cleanRepository, { recursive: true, force: true }));
  const dirtyFixture = createEvidenceFixture(cleanRepository);
  t.after(() => rmSync(dirtyFixture.root, { recursive: true, force: true }));
  dirtyFixture.document.worktreeState = 'CLEAN_SHA';
  dirtyFixture.document.stage = 'R2.7';
  dirtyFixture.document.evidenceClass = 'REPOSITORY_PLAYWRIGHT';
  dirtyFixture.document.hostKind = 'PLAYWRIGHT_CHROMIUM';
  dirtyFixture.document.claimScope = 'FORMAL_CHROMIUM';
  writeFileSync(join(cleanRepository, 'untracked.txt'), 'residue\n', 'utf8');
  errors = validateEvidenceManifest(dirtyFixture.document, {
    repositoryRoot: cleanRepository,
    manifestPath: join(dirtyFixture.root, 'manifest.json'),
    formal: true
  });
  assert.ok(errors.some(error => error.includes('completely clean worktree')));
});

test('R2 evidence rejects missing fields, screenshot hash drift, viewport drift, and cleanup leaks', t => {
  const fixture = createEvidenceFixture();
  t.after(() => rmSync(fixture.root, { recursive: true, force: true }));
  delete fixture.document.fixture;
  fixture.document.screenshots.after.sha256 = '0'.repeat(64);
  fixture.document.screenshots.after.width = 1366;
  fixture.document.cleanup.listeners = 1;
  const errors = validateEvidenceManifest(fixture.document, { repositoryRoot, manifestPath: join(fixture.root, 'manifest.json') });
  assert.ok(errors.some(error => error.includes('fixture is required')));
  assert.ok(errors.some(error => error.includes('SHA-256 mismatch')));
  assert.ok(errors.some(error => error.includes('dimensions must match')));
  assert.ok(errors.some(error => error.includes('cleanup.listeners must be 0')));
});

test('R2 evidence rejects screenshot and report paths outside its evidence directory', t => {
  const fixture = createEvidenceFixture();
  const outsideRoot = mkdtempSync(join(tmpdir(), 'clearvision-r2-outside-evidence-'));
  const outsidePng = join(outsideRoot, 'outside.png');
  writeFileSync(outsidePng, createPng(1920, 1080));
  t.after(() => {
    rmSync(fixture.root, { recursive: true, force: true });
    rmSync(outsideRoot, { recursive: true, force: true });
  });

  fixture.document.screenshots.before.path = outsidePng;
  fixture.document.reports.domBefore.path = '../outside.json';
  const errors = validateEvidenceManifest(fixture.document, {
    repositoryRoot,
    manifestPath: join(fixture.root, 'manifest.json')
  });
  assert.ok(errors.some(error => error.includes('screenshot path must stay inside')));
  assert.ok(errors.some(error => error.includes('reports.domBefore path must stay inside')));
});

test('R2 evidence binds report content and identity instead of trusting a path', t => {
  const fixture = createEvidenceFixture();
  t.after(() => rmSync(fixture.root, { recursive: true, force: true }));
  replaceReport(fixture, 'domBefore', report => {
    report.pairId = 'forged-pair';
  });
  replaceReport(fixture, 'interaction', report => {
    report.runtime.requests = ['POST /api/projects/forged/save'];
  });
  const errors = validateEvidenceManifest(fixture.document, {
    repositoryRoot,
    manifestPath: join(fixture.root, 'manifest.json')
  });
  assert.ok(errors.some(error => error.includes('reports.domBefore.pairId')));
  assert.ok(errors.some(error => error.includes('reports.interaction.runtime')));
});

test('R2 evidence rejects duplicate capabilities and unmounted active owners', t => {
  const fixture = createEvidenceFixture();
  t.after(() => rmSync(fixture.root, { recursive: true, force: true }));
  fixture.document.ownerLedger.push({ capability: 'design-lab', mounted: 0, subscriptions: 1, writes: 1 });
  const errors = validateEvidenceManifest(fixture.document, {
    repositoryRoot,
    manifestPath: join(fixture.root, 'manifest.json')
  });
  assert.ok(errors.some(error => error.includes('capability design-lab must be unique')));
  assert.ok(errors.some(error => error.includes('cannot subscribe or write while unmounted')));
});

test('R2 evidence independently rejects forged write allowances and unexpected HTTP responses', t => {
  const fixture = createEvidenceFixture();
  t.after(() => rmSync(fixture.root, { recursive: true, force: true }));
  fixture.document.runtime.requests.push('POST /api/projects/forged/save');
  fixture.document.runtime.allowedWrites.push('POST /api/projects/forged/save');
  fixture.document.runtime.httpErrors.push('GET /api/projects: 500');
  replaceReport(fixture, 'interaction', report => {
    report.runtime = fixture.document.runtime;
  });
  const errors = validateEvidenceManifest(fixture.document, {
    repositoryRoot,
    manifestPath: join(fixture.root, 'manifest.json')
  });
  assert.ok(errors.some(error => error.includes('Unapproved write allowances')));
  assert.ok(errors.some(error => error.includes('httpErrors must be empty')));
});

test('R2 matrix capture accepts an operable action and rejects missing, disabled, or obscured actions', () => {
  assert.doesNotThrow(() => validateMatrixCapture(createMatrixCapture(), createMatrixDom()));

  const missing = createMatrixCapture();
  missing.requiredCriticalActions = [];
  assert.throws(() => validateMatrixCapture(missing, createMatrixDom()), /required critical actions are missing/);

  const disabled = createMatrixDom();
  disabled.criticalActions[0].enabled = false;
  assert.throws(() => validateMatrixCapture(createMatrixCapture(), disabled), /is not operable/);

  const obscured = createMatrixDom();
  obscured.criticalActions[0].unobscured = false;
  assert.throws(() => validateMatrixCapture(createMatrixCapture(), obscured), /is not operable/);

  const offscreen = createMatrixDom();
  offscreen.criticalActions[0].inViewport = false;
  assert.throws(() => validateMatrixCapture(createMatrixCapture(), offscreen), /is not operable/);
});

test('R2 matrix capture rejects forged write allowances and unexpected HTTP responses', () => {
  const forgedWrite = createMatrixCapture();
  forgedWrite.requests.push('POST /api/projects/forged/save');
  forgedWrite.allowedWrites.push('POST /api/projects/forged/save');
  assert.throws(() => validateMatrixCapture(forgedWrite, createMatrixDom()), /unapproved write allowances/);

  const unexpectedHttp = createMatrixCapture();
  unexpectedHttp.runtime.httpErrors.push('GET /api/projects: 500');
  assert.throws(() => validateMatrixCapture(unexpectedHttp, createMatrixDom()), /runtime.httpErrors must be empty/);
});

test('R2 S05 evidence requires readable multicolor canvas pixels and valid ROI geometry', () => {
  assert.doesNotThrow(() => validateS05BitmapEvidence(createS05BitmapCapture()));

  assert.throws(() => validateS05BitmapEvidence({ scene: 'S05', variant: 'B2' }), /bitmap evidence is required/);
  const unreadable = createS05BitmapCapture();
  unreadable.bitmapEvidence.capture.canvas.readable = false;
  unreadable.bitmapEvidence.capture.canvas.error = 'tainted';
  assert.throws(() => validateS05BitmapEvidence(unreadable), /canvas pixels are unreadable/);

  const blank = createS05BitmapCapture();
  blank.bitmapEvidence.capture.canvas.nonTransparentPixels = 0;
  assert.throws(() => validateS05BitmapEvidence(blank), /canvas is blank/);

  const monochrome = createS05BitmapCapture();
  monochrome.bitmapEvidence.capture.canvas.uniqueColorCount = 1;
  assert.throws(() => validateS05BitmapEvidence(monochrome), /multicolor bitmap/);

  const missingLength = createS05BitmapCapture();
  delete missingLength.bitmapEvidence.capture.contract.byteLength;
  assert.throws(() => validateS05BitmapEvidence(missingLength), /contract does not match/);

  const unrelatedCanvas = createS05BitmapCapture();
  unrelatedCanvas.bitmapEvidence.capture.canvas.sourceSamples[0].observed = [240, 10, 10, 255];
  unrelatedCanvas.bitmapEvidence.capture.canvas.sourceSamples[0].maxChannelDelta = 216;
  assert.throws(() => validateS05BitmapEvidence(unrelatedCanvas), /do not match the canonical source samples/);

  const outside = createS05BitmapCapture();
  outside.bitmapEvidence.capture.roi.x = 90;
  assert.throws(() => validateS05BitmapEvidence(outside), /outside the canonical bitmap/);
});

test('R2 S05 baseline records legacy framing defects while final evidence rejects them', () => {
  const legacy = createS05BitmapCapture('EXCEPTION');
  legacy.bitmapEvidence.beforeAction.canvas.inViewport = false;
  legacy.bitmapEvidence.capture.canvas.inViewport = false;
  legacy.bitmapEvidence.capture.canvas.uniqueColorCount = 1;
  legacy.bitmapEvidence.capture.canvas.sourceSamples = [];

  assert.doesNotThrow(() => validateS05BitmapEvidence(legacy, 'before'));
  assert.throws(() => validateS05BitmapEvidence(legacy, 'final'), /not visibly framed|multicolor bitmap/);
});

test('R2 S05 baseline records source sample drift while final evidence rejects it', () => {
  const legacy = createS05BitmapCapture('B0');
  legacy.bitmapEvidence.beforeAction.canvas.sourceSamples[0].observed = [245, 245, 245, 255];
  legacy.bitmapEvidence.beforeAction.canvas.sourceSamples[0].maxChannelDelta = 221;
  legacy.bitmapEvidence.capture.canvas.sourceSamples[0].observed = [245, 245, 245, 255];
  legacy.bitmapEvidence.capture.canvas.sourceSamples[0].maxChannelDelta = 221;

  assert.doesNotThrow(() => validateS05BitmapEvidence(legacy, 'before'));
  assert.throws(() => validateS05BitmapEvidence(legacy, 'final'), /do not match the canonical source samples/);

  legacy.bitmapEvidence.beforeAction.canvas.sourceSamples[0].backingX = -1;
  assert.throws(() => validateS05BitmapEvidence(legacy, 'before'), /do not match the canonical source samples/);
});

test('R2 S05 B2 requires editing state and a rendered ROI overlay', () => {
  const unchanged = createS05BitmapCapture();
  unchanged.bitmapEvidence.capture.canvas.sha256 = unchanged.bitmapEvidence.beforeAction.canvas.sha256;
  assert.throws(() => validateS05BitmapEvidence(unchanged), /overlay did not change/);

  const notEditing = createS05BitmapCapture();
  notEditing.bitmapEvidence.capture.roi.phase = 'ready';
  assert.throws(() => validateS05BitmapEvidence(notEditing), /must capture the ROI editing phase/);
});

test('R2 S05 exception requires a changed but in-bounds stale ROI', () => {
  assert.doesNotThrow(() => validateS05BitmapEvidence(createS05BitmapCapture('EXCEPTION')));
  const unchanged = createS05BitmapCapture('EXCEPTION');
  unchanged.bitmapEvidence.capture.roi.x = 10;
  assert.throws(() => validateS05BitmapEvidence(unchanged), /changed stale ROI/);
});

test('R2 S05 source artifact binds one runtime GET to the route fulfill response snapshot', () => {
  const artifactId = 'a'.repeat(43);
  const url = `http://127.0.0.1:5318/api/preview-artifacts/${artifactId}`;
  const source = {
    path: 'source-final.png',
    sha256: '743aefc2299260923475866134e9218560b182d92bfc282660f9c0e4f18ea815',
    byteLength: 2384,
    contentType: 'image/png',
    status: 200,
    response: {
      source: 'PLAYWRIGHT_ROUTE_FULFILL_RESPONSE_SNAPSHOT',
      method: 'GET',
      path: `/api/preview-artifacts/${artifactId}`,
      url,
      status: 200,
      contentType: 'image/png',
      sha256: '743aefc2299260923475866134e9218560b182d92bfc282660f9c0e4f18ea815',
      byteLength: 2384
    }
  };
  const capture = {
    runtimeRequests: [{ method: 'GET', path: source.response.path, url }]
  };
  assert.doesNotThrow(() => validateS05SourceBinding(capture, source, 'final'));

  capture.runtimeRequests = [];
  assert.throws(() => validateS05SourceBinding(capture, source, 'final'), /exactly one matching runtime GET/);
  capture.runtimeRequests = [
    { method: 'GET', path: source.response.path, url },
    { method: 'GET', path: source.response.path, url }
  ];
  assert.throws(() => validateS05SourceBinding(capture, source, 'final'), /found 2/);

  capture.runtimeRequests = [{ method: 'GET', path: source.response.path, url }];
  source.response.sha256 = '0'.repeat(64);
  assert.throws(() => validateS05SourceBinding(capture, source, 'final'), /valid route fulfill response snapshot/);
});

test('R2 matrix aggregate cannot pass with false or mismatched cleanup claims', t => {
  const runRoot = mkdtempSync(join(tmpdir(), 'clearvision-r2-aggregate-'));
  t.after(() => rmSync(runRoot, { recursive: true, force: true }));
  const cleanup = {
    before: { port: 5319, endpointUnreachable: false, listenerReleased: true },
    final: { port: 9999, endpointUnreachable: true, listenerReleased: false }
  };
  const cleanupErrors = validateMatrixCleanup(cleanup, { before: 5319, final: 5318 });
  assert.ok(cleanupErrors.some(error => error.includes('before.endpointUnreachable')));
  assert.ok(cleanupErrors.some(error => error.includes('final.port')));
  assert.ok(cleanupErrors.some(error => error.includes('final.listenerReleased')));

  const summary = aggregateFinalMatrix({
    runId: 'cleanup-negative',
    runRoot,
    headSha: '1'.repeat(40),
    candidateContentHash: '2'.repeat(64),
    candidateWorktreeState: 'DIRTY_CANDIDATE',
    fixtureHash: '3'.repeat(64),
    baselineBuildHash: '4'.repeat(64),
    candidateBuildHash: '5'.repeat(64),
    beforePort: 5319,
    finalPort: 5318,
    cleanup
  });
  assert.equal(summary.status, 'FAIL');
  assert.ok(summary.errors.some(error => error.includes('serverCleanup.before.endpointUnreachable')));
});

test('R2 matrix aggregate rejects all-zero baseline and candidate build hashes', t => {
  const runRoot = mkdtempSync(join(tmpdir(), 'clearvision-r2-zero-build-hash-'));
  t.after(() => rmSync(runRoot, { recursive: true, force: true }));
  const summary = aggregateFinalMatrix({
    runId: 'zero-build-hash-negative',
    runRoot,
    headSha: '1'.repeat(40),
    candidateContentHash: '2'.repeat(64),
    candidateWorktreeState: 'DIRTY_CANDIDATE',
    fixtureHash: '3'.repeat(64),
    baselineBuildHash: '0'.repeat(64),
    candidateBuildHash: '0'.repeat(64),
    beforePort: 5319,
    finalPort: 5318,
    cleanup: {
      before: { port: 5319, endpointUnreachable: true, listenerReleased: true },
      final: { port: 5318, endpointUnreachable: true, listenerReleased: true }
    }
  });
  assert.equal(summary.status, 'FAIL');
  assert.ok(summary.errors.some(error => error.startsWith('baselineBuildHash must be a non-zero SHA-256')));
  assert.ok(summary.errors.some(error => error.startsWith('candidateBuildHash must be a non-zero SHA-256')));
});

test('R2 evidence rejects a stale shared fixture hash', t => {
  const fixture = createEvidenceFixture();
  t.after(() => rmSync(fixture.root, { recursive: true, force: true }));
  fixture.document.fixture.hash = '0'.repeat(64);
  const errors = validateEvidenceManifest(fixture.document, {
    repositoryRoot,
    manifestPath: join(fixture.root, 'manifest.json')
  });
  assert.ok(errors.some(error => error.includes('fixture.hash does not match')));
});

test('R2 evidence rejects secret-bearing keys and a forged WebView2 125 claim', t => {
  const fixture = createEvidenceFixture();
  t.after(() => rmSync(fixture.root, { recursive: true, force: true }));
  fixture.document.evidenceClass = 'WINFORMS_WEBVIEW2';
  fixture.document.hostKind = 'WINFORMS_WEBVIEW2';
  fixture.document.claimScope = 'WEBVIEW2_125';
  fixture.document.nativeDpi = 96;
  fixture.document.runtime.accessToken = 'forbidden';
  const errors = validateEvidenceManifest(fixture.document, { repositoryRoot, manifestPath: join(fixture.root, 'manifest.json') });
  assert.ok(errors.some(error => error.includes('not allowed')));
  assert.ok(errors.some(error => error.includes('secret-bearing')));
  assert.ok(errors.some(error => error.includes('requires nativeDpi=120')));
});

test('R2 evidence rejects iteration four and NON_COMPARABLE without reasons', t => {
  const fixture = createEvidenceFixture();
  t.after(() => rmSync(fixture.root, { recursive: true, force: true }));
  fixture.document.iteration = 4;
  fixture.document.comparability = 'NON_COMPARABLE';
  const errors = validateEvidenceManifest(fixture.document, { repositoryRoot, manifestPath: join(fixture.root, 'manifest.json') });
  assert.ok(errors.some(error => error.includes('iteration must be <= 3')));
  assert.ok(errors.some(error => error.includes('requires a reason')));
});

test('R2 evidence rejects an unproven fixture server cleanup claim', t => {
  const fixture = createEvidenceFixture();
  t.after(() => rmSync(fixture.root, { recursive: true, force: true }));
  fixture.document.cleanup.ownershipMarkerVerified = false;
  fixture.document.cleanup.endpointUnreachable = false;
  fixture.document.cleanup.pidExited = false;
  const errors = validateEvidenceManifest(fixture.document, { repositoryRoot, manifestPath: join(fixture.root, 'manifest.json') });
  assert.ok(errors.some(error => error.includes('ownership marker')));
  assert.ok(errors.some(error => error.includes('endpoint is unreachable')));
  assert.ok(errors.some(error => error.includes('PID exited')));
});

test('R2 blind review enforces reviewer completeness, group count, and two-thirds final preference', () => {
  assert.deepEqual(validateBlindReview(createReview()), []);
  const insufficient = createReview(3, ['reviewer-a']);
  insufficient.groups[0].votes[0].preference = 'left';
  const errors = validateBlindReview(insufficient);
  assert.ok(errors.some(error => error.includes('at least 2 reviewers')));
  assert.ok(errors.some(error => error.includes('at least 4 groups')));
  assert.ok(errors.some(error => error.includes('passed')));
});

test('R2 blind review requires bilateral scores for no-difference votes', () => {
  const document = createReview();
  document.groups[0].votes[0].preference = 'no-difference';
  const errors = validateBlindReview(document);
  assert.ok(errors.some(error => error.includes('leftScores and rightScores')));
});

test('R2 blind review accepts bilateral scores without a synthetic selected score', () => {
  const document = createReview(4, ['reviewer-a', 'reviewer-b', 'reviewer-c']);
  const vote = document.groups[0].votes[0];
  vote.preference = 'no-difference';
  delete vote.scores;
  vote.leftScores = reviewScores();
  vote.rightScores = reviewScores();
  assert.deepEqual(validateBlindReview(document), []);
});

test('R2 FINAL review requires exactly S00-S13 by B0, B2 and exception with 36 passing groups', () => {
  assert.deepEqual(validateBlindReview(createFinalReview(36)), []);

  const only35Pass = createFinalReview(35);
  assert.ok(validateBlindReview(only35Pass).some(error => error.includes('36 are required')));

  const missingGroup = createFinalReview();
  missingGroup.groups.pop();
  const missingErrors = validateBlindReview(missingGroup);
  assert.ok(missingErrors.some(error => error.includes('exactly 42 groups')));
  assert.ok(missingErrors.some(error => error.includes('exactly 3 groups for S13')));

  const duplicateVariant = createFinalReview();
  duplicateVariant.groups[2].variant = 'B2';
  assert.ok(validateBlindReview(duplicateVariant).some(error => error.includes('one EXCEPTION group for S00')));

  const duplicatePair = createFinalReview();
  duplicatePair.groups[1].pairId = duplicatePair.groups[0].pairId;
  assert.ok(validateBlindReview(duplicatePair).some(error => error.includes('pairId must be unique')));

  const misboundPair = createFinalReview();
  misboundPair.groups[0].pairId = 'S13-EXCEPTION-wrong';
  assert.ok(validateBlindReview(misboundPair).some(error => error.includes('must bind to S00-B0')));
});

test('R2 FINAL review checks every score dimension independently', () => {
  const document = createFinalReview();
  for (const group of document.groups) {
    for (const vote of group.votes) vote.scores.material_hierarchy = 3;
  }
  const errors = validateBlindReview(document);
  assert.ok(errors.some(error => error.includes('material_hierarchy review score median')));
});

test('R2 motion inventory accepts tokenized bounded state motion', () => {
  assert.deepEqual(validateMotionInventory(createMotion()), []);
});

test('R2 motion inventory binds to the current motion-bearing source snapshot', t => {
  const sourceRoot = mkdtempSync(join(tmpdir(), 'clearvision-r2-motion-'));
  t.after(() => rmSync(sourceRoot, { recursive: true, force: true }));
  mkdirSync(join(sourceRoot, 'controls'), { recursive: true });
  writeFileSync(join(sourceRoot, 'controls', 'Menu.vue'), '<style scoped>.menu { transition: opacity 140ms ease; }</style>\n', 'utf8');
  const snapshot = computeMotionSourceSnapshot(sourceRoot);
  const document = createMotion({ sourceRefs: ['controls/Menu.vue'] });
  document.sourceHash = snapshot.hash;
  document.sourceFiles = [...snapshot.files];
  assert.deepEqual(validateMotionInventory(document, { sourceRoot }), []);

  document.sourceHash = '0'.repeat(64);
  assert.ok(validateMotionInventory(document, { sourceRoot }).some(error => error.includes('sourceHash is stale')));
});

test('R2 motion source scan rejects unowned sources, layout transitions and non-loading loops', t => {
  const sourceRoot = mkdtempSync(join(tmpdir(), 'clearvision-r2-motion-negative-'));
  t.after(() => rmSync(sourceRoot, { recursive: true, force: true }));
  writeFileSync(join(sourceRoot, 'Unsafe.vue'), '<style>.unsafe { transition: width 140ms ease; animation: drift 1s linear infinite; }</style>\n', 'utf8');
  const snapshot = computeMotionSourceSnapshot(sourceRoot);
  const document = createMotion();
  document.sourceHash = snapshot.hash;
  document.sourceFiles = [...snapshot.files];
  document.items[0].sourceRefs = ['missing.vue'];
  const errors = validateMotionInventory(document, { sourceRoot });
  assert.ok(errors.some(error => error.includes('not assigned')));
  assert.ok(errors.some(error => error.includes('missing or no longer')));
  assert.ok(errors.some(error => error.includes('layout property width')));
  assert.ok(errors.some(error => error.includes('non-loading infinite animation')));
});

test('R2 motion source scan rejects named infinite animations and unrelated cleanup text', t => {
  const sourceRoot = mkdtempSync(join(tmpdir(), 'clearvision-r2-motion-paired-cleanup-'));
  t.after(() => rmSync(sourceRoot, { recursive: true, force: true }));
  writeFileSync(join(sourceRoot, 'Unsafe.vue'), `
<script setup>
function mountOwner() {
  window.addEventListener('resize', updatePosition);
  const refreshTimer = setInterval(refresh, 1000);
}
function unrelatedCleanup() {
  window.removeEventListener('scroll', updatePosition);
  clearInterval(otherTimer);
}
</script>
<style>.ambient { animation-name: drift; animation-duration: 1s; animation-iteration-count: infinite }</style>
`, 'utf8');
  const snapshot = computeMotionSourceSnapshot(sourceRoot);
  const document = createMotion({ sourceRefs: ['Unsafe.vue'] });
  document.sourceHash = snapshot.hash;
  document.sourceFiles = [...snapshot.files];
  const errors = validateMotionInventory(document, { sourceRoot });
  assert.ok(errors.some(error => error.includes('non-loading infinite animation')));
  assert.ok(errors.some(error => error.includes('window.resize:updatePosition')));
  assert.ok(errors.some(error => error.includes('refreshTimer')));
});

test('R2 motion inventory rejects layout animation, unstable keys, and Canvas forbidden targets', () => {
  const document = createMotion({
    properties: ['width', 'transform'],
    durationMs: 300,
    stableKey: 'array-index',
    target: '.flow-canvas-host',
    risk: 'FORBIDDEN_ZONE'
  });
  const errors = validateMotionInventory(document);
  assert.ok(errors.some(error => error.includes('forbidden property width')));
  assert.ok(errors.some(error => error.includes('200ms')));
  assert.ok(errors.some(error => error.includes('unstable key')));
  assert.ok(errors.some(error => error.includes('forbidden target')));
});

test('R2 native 125 validator rejects 96 DPI and accepts observed native 120 DPI', () => {
  assert.ok(validateNative125Evidence({ screenshot: { DPI_TYPE: 'NATIVE_WINDOW_DPI_OBSERVED', nativeWindow: { dpi: 96 } } }).length > 0);
  assert.deepEqual(validateNative125Evidence({ screenshot: { DPI_TYPE: 'NATIVE_WINDOW_DPI_OBSERVED', nativeWindow: { dpi: 120 } } }), []);
});

test('R2 no-Node validator rejects local sanitized-path evidence and accepts an independent published target', () => {
  assert.ok(validateIndependentNoNodeEvidence({ cleanMachineWithoutNode: { status: 'NOT_PERFORMED' } }).length > 0);
  assert.deepEqual(validateIndependentNoNodeEvidence({
    cleanMachineWithoutNode: {
      status: 'PASS',
      nodeInstalled: false,
      sameMachineAsEvidenceDriver: false,
      runtimeKind: 'PUBLISHED_RELEASE',
      productProcessTree: { nodeDescendantCount: 0 }
    }
  }), []);
});

test('R2 browser session rejects output outside the owned r2 fixture root', () => {
  const document = {
    schemaVersion: 'r2-in-app-browser.v1',
    status: 'READY',
    readyUrl: 'http://127.0.0.1:5177/studio/index.html',
    pid: 123,
    port: 5177,
    serverOwner: 'R2_BROWSER_FIXTURE_SESSION',
    sourceSha: '1'.repeat(40),
    candidateContentHash: '2'.repeat(64),
    fixtureHash: '3'.repeat(64),
    createdByBatch: 'r2.0-baseline',
    cleanupToken: '4'.repeat(32),
    fixtureRoot: resolve(repositoryRoot, 'output', 'browser-fixture'),
    startedAtUtc: new Date().toISOString()
  };
  assert.ok(validateBrowserSession(document).some(error => error.includes('fixture root')));
});

test('R2 browser session accepts the repository-owned r2 fixture root', () => {
  const document = {
    schemaVersion: 'r2-in-app-browser.v1',
    status: 'READY',
    readyUrl: 'http://127.0.0.1:5177/studio/index.html',
    pid: 123,
    port: 5177,
    serverOwner: 'R2_BROWSER_FIXTURE_SESSION',
    sourceSha: '1'.repeat(40),
    candidateContentHash: '2'.repeat(64),
    fixtureHash: computeFixtureHash(repositoryRoot),
    createdByBatch: 'r2.0-baseline',
    cleanupToken: '4'.repeat(32),
    fixtureRoot: resolve(repositoryRoot, '.tmp', 'studio-ui-next', 'r2', 'browser-fixture'),
    startedAtUtc: new Date().toISOString()
  };
  assert.deepEqual(validateBrowserSession(document, { repositoryRoot }), []);

  document.fixtureHash = '0'.repeat(64);
  assert.ok(validateBrowserSession(document, { repositoryRoot }).some(error => error.includes('fixtureHash does not match')));
});

test('R2 fixture hash covers the matrix harness but excludes its stored hash field', t => {
  const fixtureRepository = mkdtempSync(join(tmpdir(), 'clearvision-r2-fixture-hash-'));
  t.after(() => rmSync(fixtureRepository, { recursive: true, force: true }));
  for (const file of r2FixtureFiles) {
    const target = join(fixtureRepository, file);
    mkdirSync(resolve(target, '..'), { recursive: true });
    writeFileSync(target, readFileSync(join(repositoryRoot, file)));
  }

  const before = computeFixtureHash(fixtureRepository);
  const capabilityPath = join(
    fixtureRepository,
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual/browser-fixture-capability.json'
  );
  const capability = JSON.parse(readFileSync(capabilityPath, 'utf8'));
  capability.fixtureHash = 'f'.repeat(64);
  writeFileSync(capabilityPath, `${JSON.stringify(capability, null, 2)}\n`, 'utf8');
  assert.equal(computeFixtureHash(fixtureRepository), before);

  const matrixPath = join(
    fixtureRepository,
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual/r2-final-matrix-evidence.ts'
  );
  writeFileSync(matrixPath, `${readFileSync(matrixPath, 'utf8')}\n// fixture drift\n`, 'utf8');
  assert.notEqual(computeFixtureHash(fixtureRepository), before);
});

test('R2 browser session proves live marker ownership and tokenized cleanup', { timeout: 240_000 }, async () => {
  const port = await reservePort();
  let session;
  try {
    session = await startSession({ port, batch: 'r2.0-unit-lifecycle' });
    assert.equal((await statusSession()).status, 'READY');
    await assert.rejects(
      stopSession({ cleanupToken: '0'.repeat(48) }),
      /cleanup token does not match/i
    );

    const markerPath = resolve(getSessionPaths().webRoot, 'studio', '.r2-session.json');
    const originalMarker = readFileSync(markerPath, 'utf8');
    const forgedMarker = { ...JSON.parse(originalMarker), cleanupToken: 'f'.repeat(48) };
    writeFileSync(markerPath, `${JSON.stringify(forgedMarker, null, 2)}\n`, 'utf8');
    const failedStatus = await statusSession();
    assert.equal(failedStatus.status, 'FAILED');
    assert.ok(failedStatus.errors.some(error => /cleanup token does not match/i.test(error)));
    writeFileSync(markerPath, originalMarker, 'utf8');

    const stopped = await stopSession({ cleanupToken: session.cleanupToken });
    assert.equal(stopped.status, 'STOPPED');
    session = null;
    assert.equal((await statusSession()).status, 'STOPPED');
    await assert.rejects(fetch(`http://127.0.0.1:${port}/studio/.r2-session.json`));
  } finally {
    if (session) {
      await stopSession({ cleanupToken: session.cleanupToken }).catch(() => undefined);
    }
  }
});

test('R2 candidate content hash is deterministic and SHA-256 shaped', () => {
  const first = computeCandidateContentHash(repositoryRoot);
  const second = computeCandidateContentHash(repositoryRoot);
  assert.match(first, /^[0-9a-f]{64}$/);
  assert.equal(first, second);
});

test('R2 candidate content hash includes untracked UTF-8 R2 ledger content', t => {
  const cleanRepository = createCleanGitRepository();
  const ledgerDirectory = join(cleanRepository, 'docs', '进行中', 'StudioUINext');
  const ledgerPath = join(ledgerDirectory, 'R2_视觉精修执行台账.md');
  t.after(() => rmSync(cleanRepository, { recursive: true, force: true }));
  mkdirSync(ledgerDirectory, { recursive: true });
  writeFileSync(ledgerPath, '第一版\n', 'utf8');
  const first = computeCandidateContentHash(cleanRepository);
  writeFileSync(ledgerPath, '第二版\n', 'utf8');
  const second = computeCandidateContentHash(cleanRepository);
  assert.notEqual(first, second);
});

function git(cwd, args) {
  return execFileSync('git', args, { cwd, encoding: 'utf8' });
}

function createCleanGitRepository() {
  const root = mkdtempSync(join(tmpdir(), 'clearvision-r2-clean-repository-'));
  mkdirSync(join(root, 'ClearVision.Product'), { recursive: true });
  writeFileSync(join(root, 'ClearVision.Product', 'candidate.txt'), 'clean candidate\n', 'utf8');
  git(root, ['init', '--quiet']);
  git(root, ['config', 'user.name', 'R2 Test']);
  git(root, ['config', 'user.email', 'r2-test@clearvision.invalid']);
  git(root, ['add', '.']);
  git(root, ['commit', '--quiet', '-m', 'fixture']);
  return root;
}

async function reservePort() {
  return new Promise((resolvePromise, reject) => {
    const server = createServer();
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      if (!address || typeof address === 'string') {
        server.close();
        reject(new Error('Unable to reserve an R2 fixture port.'));
        return;
      }
      server.close(error => error ? reject(error) : resolvePromise(address.port));
    });
  });
}
