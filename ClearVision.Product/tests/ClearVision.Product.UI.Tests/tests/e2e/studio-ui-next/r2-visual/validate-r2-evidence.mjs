import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, isAbsolute, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { inspectPngBuffer } from './r2-png.mjs';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const schemaPaths = Object.freeze({
  manifest: join(scriptDirectory, 'r2-evidence.schema.json'),
  review: join(scriptDirectory, 'r2-review.schema.json'),
  motion: join(scriptDirectory, 'r2-motion.schema.json'),
  browser: join(scriptDirectory, 'r2-in-app-browser.schema.json')
});

const claimContracts = Object.freeze({
  IN_APP_BROWSER_ITERATION: Object.freeze({
    hostKind: 'IN_APP_BROWSER',
    claimScopes: Object.freeze(['DIRECTIONAL_BROWSER']),
    nativeDpi: 'NOT_PERFORMED'
  }),
  REPOSITORY_PLAYWRIGHT: Object.freeze({
    hostKind: 'PLAYWRIGHT_CHROMIUM',
    claimScopes: Object.freeze(['FORMAL_CHROMIUM']),
    nativeDpi: 'NOT_PERFORMED'
  }),
  WINFORMS_WEBVIEW2: Object.freeze({
    hostKind: 'WINFORMS_WEBVIEW2',
    claimScopes: Object.freeze(['WEBVIEW2_100', 'WEBVIEW2_125', 'NO_NODE_TARGET', 'FIELD'])
  })
});

const permittedMotionProperties = new Set([
  'opacity',
  'transform',
  'color',
  'background-color',
  'border-color',
  'box-shadow'
]);
const forbiddenMotionProperties = /^(?:all|width|height|min-width|max-width|min-height|max-height|grid|grid-template|grid-template-columns|grid-template-rows|flex|flex-basis|inset|top|right|bottom|left|margin|padding)$/i;
const forbiddenMotionTarget = /(?:flowcanvas|flow-canvas|canvas-host|image-viewport|imageviewport|roi-pointer|roi-coordinate|camera-frame|canvas-backing|splitter|pane-size|sse-row|dense-table-row)/i;
const secretKey = /(?:cookie|password|authorization|access.?token|refresh.?token|local.?storage|session.?storage|secret)/i;
const reviewDimensions = Object.freeze([
  'visual_focus',
  'material_hierarchy',
  'operation_priority',
  'cross_page_consistency'
]);
const finalScenes = Object.freeze(Array.from({ length: 14 }, (_, index) => `S${String(index).padStart(2, '0')}`));
const finalVariants = Object.freeze(['B0', 'B2', 'EXCEPTION']);
const approvedWritesByScene = readJson(join(scriptDirectory, 'r2-approved-writes.json')).scenes;

export function findRepositoryRoot(startPath = scriptDirectory) {
  let current = resolve(startPath);
  while (true) {
    if (existsSync(join(current, '.git')) && existsSync(join(current, 'ClearVision.Product'))) {
      return current;
    }
    const parent = dirname(current);
    if (parent === current) throw new Error(`Unable to find repository root from ${startPath}.`);
    current = parent;
  }
}

export function readJson(path) {
  return JSON.parse(readFileSync(path, 'utf8'));
}

export function validateAgainstSchema(document, schema) {
  const errors = [];
  validateSchemaNode(document, schema, schema, '$', errors);
  return errors;
}

function validateSchemaNode(value, node, rootSchema, path, errors) {
  if (node.$ref) {
    const target = resolveSchemaReference(rootSchema, node.$ref);
    validateSchemaNode(value, target, rootSchema, path, errors);
    return;
  }
  if (node.oneOf) {
    const matches = node.oneOf.filter(candidate => {
      const candidateErrors = [];
      validateSchemaNode(value, candidate, rootSchema, path, candidateErrors);
      return candidateErrors.length === 0;
    });
    if (matches.length !== 1) errors.push(`${path} must match exactly one schema in oneOf.`);
    return;
  }
  if (Object.hasOwn(node, 'const') && !deepEqual(value, node.const)) {
    errors.push(`${path} must equal ${JSON.stringify(node.const)}.`);
  }
  if (node.enum && !node.enum.some(item => deepEqual(item, value))) {
    errors.push(`${path} must be one of ${node.enum.map(item => JSON.stringify(item)).join(', ')}.`);
  }
  if (node.type && !matchesType(value, node.type)) {
    errors.push(`${path} must be ${node.type}.`);
    return;
  }
  if (value && typeof value === 'object' && !Array.isArray(value)) {
    const required = node.required ?? [];
    for (const property of required) {
      if (!Object.hasOwn(value, property)) errors.push(`${path}.${property} is required.`);
    }
    if (node.additionalProperties === false && node.properties) {
      for (const property of Object.keys(value)) {
        if (!Object.hasOwn(node.properties, property)) errors.push(`${path}.${property} is not allowed.`);
      }
    }
    for (const [property, propertyValue] of Object.entries(value)) {
      if (node.properties?.[property]) {
        validateSchemaNode(propertyValue, node.properties[property], rootSchema, `${path}.${property}`, errors);
      } else if (node.additionalProperties && typeof node.additionalProperties === 'object') {
        validateSchemaNode(propertyValue, node.additionalProperties, rootSchema, `${path}.${property}`, errors);
      }
    }
  }
  if (Array.isArray(value)) {
    if (node.minItems !== undefined && value.length < node.minItems) errors.push(`${path} must contain at least ${node.minItems} items.`);
    if (node.maxItems !== undefined && value.length > node.maxItems) errors.push(`${path} must contain at most ${node.maxItems} items.`);
    if (node.uniqueItems) {
      const serialized = value.map(item => JSON.stringify(item));
      if (new Set(serialized).size !== serialized.length) errors.push(`${path} must contain unique items.`);
    }
    if (node.items) value.forEach((item, index) => validateSchemaNode(item, node.items, rootSchema, `${path}[${index}]`, errors));
  }
  if (typeof value === 'string') {
    if (node.minLength !== undefined && value.length < node.minLength) errors.push(`${path} must contain at least ${node.minLength} characters.`);
    if (node.pattern && !(new RegExp(node.pattern).test(value))) errors.push(`${path} does not match ${node.pattern}.`);
    if (node.format === 'date-time' && Number.isNaN(Date.parse(value))) errors.push(`${path} must be an ISO date-time.`);
  }
  if (typeof value === 'number') {
    if (node.minimum !== undefined && value < node.minimum) errors.push(`${path} must be >= ${node.minimum}.`);
    if (node.maximum !== undefined && value > node.maximum) errors.push(`${path} must be <= ${node.maximum}.`);
    if (node.exclusiveMinimum !== undefined && value <= node.exclusiveMinimum) errors.push(`${path} must be > ${node.exclusiveMinimum}.`);
  }
}

function resolveSchemaReference(schema, reference) {
  if (!reference.startsWith('#/')) throw new Error(`Only local schema references are supported: ${reference}.`);
  return reference.slice(2).split('/').reduce((current, segment) => current[segment.replaceAll('~1', '/').replaceAll('~0', '~')], schema);
}

function matchesType(value, type) {
  if (type === 'array') return Array.isArray(value);
  if (type === 'object') return value !== null && typeof value === 'object' && !Array.isArray(value);
  if (type === 'integer') return Number.isInteger(value);
  if (type === 'number') return typeof value === 'number' && Number.isFinite(value);
  if (type === 'null') return value === null;
  return typeof value === type;
}

function deepEqual(left, right) {
  return JSON.stringify(left) === JSON.stringify(right);
}

export function computeCandidateContentHash(repositoryRoot = findRepositoryRoot()) {
  const headSha = git(repositoryRoot, ['rev-parse', 'HEAD']).trim().toLowerCase();
  const trackedDiff = git(repositoryRoot, ['diff', '--binary', 'HEAD', '--']);
  const allowedUntracked = git(repositoryRoot, ['ls-files', '-z', '--others', '--exclude-standard'])
    .split('\0')
    .map(file => file.replaceAll('\\', '/'))
    .filter(isCandidateUntrackedAllowed)
    .sort((left, right) => left.localeCompare(right, 'en'));
  const hash = createHash('sha256');
  hash.update(`HEAD\0${headSha}\0TRACKED_DIFF\0`);
  hash.update(trackedDiff);
  for (const file of allowedUntracked) {
    const absolute = resolve(repositoryRoot, file);
    if (!existsSync(absolute) || !statSync(absolute).isFile()) continue;
    hash.update(`\0UNTRACKED\0${file.replaceAll('\\', '/')}\0`);
    hash.update(readFileSync(absolute));
  }
  return hash.digest('hex');
}

function isCandidateUntrackedAllowed(file) {
  if (file === 'TODOViewR2.md' || file === 'PRODUCT.md') return true;
  if (/^docs\/进行中\/StudioUINext\/R2_[^/]+$/.test(file)) return true;
  if (file === 'ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/design-system/primitives/CvToggle.vue') return true;
  if (file === 'ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/design-system/primitives/modalIsolation.ts') return true;
  if (file === 'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f03-preview-bitmap-fixture.ts') return true;
  if (file.startsWith('ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual/')) return true;
  return file === 'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/unit/studio-ui-next-r2-evidence.test.mjs';
}

export const r2FixtureFiles = Object.freeze([
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f02-browser-fixture.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f02-operators.spec.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f02-overview.spec.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f02-projects-read.spec.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f02-results.spec.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f02-stations.spec.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f02-support-surfaces.spec.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f03-browser-fixture.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f03-preview-bitmap-contract.json',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f03-preview-bitmap-fixture.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f03-workspace.spec.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f05-inspection-run.spec.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f06-ai-fixture.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f06-ai-workbench.spec.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f07-device-fixture.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f07-settings-shell.spec.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual/browser-fixture-capability.json',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual/r2-approved-writes.json',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual/r2-png.mjs',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual/playwright.r2-matrix.config.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual/r2-final-matrix-runner.mjs',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual/r2-final-matrix-summary.schema.json',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual/r2-final-matrix-evidence.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual/r2-in-app-browser-fixture.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual/r2-visual.spec.ts',
    'ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/r2-visual/r2-visual-fixture.ts'
  ]);

export function computeFixtureHash(repositoryRoot = findRepositoryRoot()) {
  const hash = createHash('sha256');
  for (const file of r2FixtureFiles) {
    const absolute = resolve(repositoryRoot, file);
    hash.update(`${file}\0`);
    if (!existsSync(absolute)) {
      hash.update(Buffer.from('MISSING'));
      continue;
    }
    if (file.endsWith('/browser-fixture-capability.json')) {
      const capability = readJson(absolute);
      hash.update(JSON.stringify({ ...capability, fixtureHash: 'DERIVED_BY_VALIDATOR' }));
      continue;
    }
    hash.update(readFileSync(absolute));
  }
  return hash.digest('hex');
}

function git(repositoryRoot, args) {
  return execFileSync('git', args, { cwd: repositoryRoot, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
}

export function validateEvidenceManifest(document, options = {}) {
  const repositoryRoot = options.repositoryRoot ?? findRepositoryRoot();
  const manifestPath = options.manifestPath ? resolve(options.manifestPath) : null;
  const schema = readJson(schemaPaths.manifest);
  const errors = validateAgainstSchema(document, schema);
  collectSecretKeys(document, '$', errors);

  const contract = claimContracts[document.evidenceClass];
  if (contract) {
    if (document.hostKind !== contract.hostKind) errors.push(`hostKind ${document.hostKind} is incompatible with ${document.evidenceClass}.`);
    if (!contract.claimScopes.includes(document.claimScope)) errors.push(`claimScope ${document.claimScope} is incompatible with ${document.evidenceClass}.`);
    if (contract.nativeDpi && document.nativeDpi !== contract.nativeDpi) errors.push(`${document.evidenceClass} must record nativeDpi=${contract.nativeDpi}.`);
  }
  if (document.claimScope === 'WEBVIEW2_125' && document.nativeDpi !== 120) errors.push('WEBVIEW2_125 requires nativeDpi=120.');
  if (document.claimScope === 'WEBVIEW2_100' && document.nativeDpi !== 96) errors.push('WEBVIEW2_100 requires nativeDpi=96.');
  const actualCandidateHash = computeCandidateContentHash(repositoryRoot);
  if (document.candidateContentHash !== actualCandidateHash) {
    errors.push('candidateContentHash does not match the current candidate content.');
  }
  const actualFixtureHash = computeFixtureHash(repositoryRoot);
  if (document.fixture?.hash !== actualFixtureHash) {
    errors.push('fixture.hash does not match the current shared browser fixture content.');
  }
  if (options.formal && document.worktreeState !== 'CLEAN_SHA') {
    errors.push('Formal evidence requires worktreeState=CLEAN_SHA.');
  }
  if (options.formal && document.stage !== 'R2.7') errors.push('Formal evidence must be recorded at stage R2.7.');
  if (options.formal && !['REPOSITORY_PLAYWRIGHT', 'WINFORMS_WEBVIEW2'].includes(document.evidenceClass)) {
    errors.push('Formal evidence requires Repository Playwright or WinForms WebView2 evidence.');
  }
  if (document.worktreeState === 'CLEAN_SHA' || options.formal) {
    const actualHead = git(repositoryRoot, ['rev-parse', 'HEAD']).trim().toLowerCase();
    if (document.headSha !== actualHead) errors.push(`headSha ${document.headSha} does not match current HEAD ${actualHead}.`);
    const status = git(repositoryRoot, ['status', '--porcelain', '--untracked-files=all']).trim();
    if (status) errors.push('CLEAN_SHA evidence requires a completely clean worktree, including untracked files.');
  }
  if (document.comparability === 'COMPARABLE' && document.nonComparableReasons?.length > 0) errors.push('COMPARABLE evidence cannot contain nonComparableReasons.');
  if (document.comparability === 'NON_COMPARABLE' && document.nonComparableReasons?.length === 0) errors.push('NON_COMPARABLE evidence requires a reason.');

  const before = document.screenshots?.before;
  const after = document.screenshots?.after;
  if (before && after) {
    validateArtifact(before, manifestPath, errors, 'before');
    validateArtifact(after, manifestPath, errors, 'after');
    if (document.comparability === 'COMPARABLE') {
      if (before.width !== after.width || before.height !== after.height) errors.push('Comparable screenshot dimensions must match.');
      if (before.width !== document.viewport.width || before.height !== document.viewport.height) errors.push('Comparable screenshot dimensions must match the CSS viewport.');
    }
  }
  for (const [name, artifact] of Object.entries(document.reports ?? {})) {
    validateReportArtifact(artifact, manifestPath, errors, name, document);
  }
  validateOwnerLedger(document.ownerLedger, errors);
  validateRuntimeAudit(document, errors);
  validateCriticalActions(document.requiredCriticalActions, document.metrics?.criticalActions, errors, 'metrics');
  if ((document.runtime?.consoleErrors?.length ?? 0) > 0) errors.push('consoleErrors must be empty for accepted evidence.');
  if ((document.runtime?.pageErrors?.length ?? 0) > 0) errors.push('pageErrors must be empty for accepted evidence.');
  if ((document.runtime?.failedRequests?.length ?? 0) > 0) errors.push('failedRequests must be empty for accepted evidence.');
  if ((document.runtime?.unexpectedWrites?.length ?? 0) > 0) errors.push('unexpectedWrites must be empty for accepted evidence.');
  if ((document.metrics?.horizontalOverflow ?? 0) > 0) errors.push('Page-level horizontal overflow must be 0.');
  if ((document.metrics?.unrecordedNestedScrollOwners ?? 0) > 0) errors.push('Unrecorded nested scroll owners must be 0.');
  if ((document.metrics?.criticalActionTruncationCount ?? 0) > 0) errors.push('Critical action truncation count must be 0.');
  if ((document.metrics?.criticalActionUnreachableCount ?? 0) > 0) errors.push('Critical action unreachable count must be 0.');
  if ((document.metrics?.layoutShift ?? 0) > 0) errors.push('Interaction layout shift must be 0.');
  if (document.cleanup?.status !== 'PASS') errors.push('cleanup.status must be PASS.');
  for (const field of ['timers', 'animationFrames', 'listeners', 'requests']) {
    if ((document.cleanup?.[field] ?? 0) !== 0) errors.push(`cleanup.${field} must be 0.`);
  }
  if (document.server?.createdByBatch) {
    if (!document.cleanup?.serverStopped) errors.push('A batch-created fixture server must be stopped before evidence acceptance.');
    if (!document.cleanup?.ownershipMarkerVerified) errors.push('Batch-created server cleanup must verify the live ownership marker before termination.');
    if (!document.cleanup?.endpointUnreachable) errors.push('Batch-created server cleanup must prove its endpoint is unreachable.');
    if (!document.cleanup?.pidExited) errors.push('Batch-created server cleanup must prove its PID exited.');
  }
  return errors;
}

function collectSecretKeys(value, path, errors) {
  if (!value || typeof value !== 'object') return;
  for (const [key, child] of Object.entries(value)) {
    if (key !== 'cleanupToken' && secretKey.test(key)) errors.push(`${path}.${key} may contain secret-bearing data and is forbidden.`);
    collectSecretKeys(child, `${path}.${key}`, errors);
  }
}

function validateArtifact(artifact, manifestPath, errors, label) {
  const absolute = resolveReferencedPath(artifact.path, manifestPath);
  if (!absolute) {
    errors.push(`${label} screenshot path must stay inside the evidence directory: ${artifact.path}.`);
    return;
  }
  if (!existsSync(absolute) || !statSync(absolute).isFile()) {
    errors.push(`${label} screenshot does not exist: ${artifact.path}.`);
    return;
  }
  const buffer = readFileSync(absolute);
  const hash = createHash('sha256').update(buffer).digest('hex');
  if (hash !== artifact.sha256) errors.push(`${label} screenshot SHA-256 mismatch.`);
  const dimensions = readPngDimensions(buffer);
  if (!dimensions) errors.push(`${label} screenshot is not a valid PNG.`);
  else if (dimensions.width !== artifact.width || dimensions.height !== artifact.height) errors.push(`${label} PNG dimensions do not match the manifest.`);
}

function validateReportArtifact(artifact, manifestPath, errors, name, document) {
  const label = `reports.${name}`;
  const absolute = resolveReferencedPath(artifact?.path, manifestPath);
  if (!absolute) {
    errors.push(`${label} path must stay inside the evidence directory: ${artifact?.path}.`);
    return;
  }
  if (!existsSync(absolute) || !statSync(absolute).isFile()) {
    errors.push(`${label} does not exist: ${artifact?.path}.`);
    return;
  }
  const buffer = readFileSync(absolute);
  const hash = createHash('sha256').update(buffer).digest('hex');
  if (hash !== artifact.sha256) errors.push(`${label} SHA-256 mismatch.`);
  let report;
  try {
    report = JSON.parse(buffer.toString('utf8'));
  } catch (error) {
    errors.push(`${label} is not valid JSON: ${error instanceof Error ? error.message : String(error)}.`);
    return;
  }
  for (const identity of ['scene', 'pairId', 'route', 'state']) {
    if (report?.[identity] !== document[identity]) errors.push(`${label}.${identity} does not match the manifest.`);
  }
  if (name === 'domBefore' || name === 'domAfter') {
    if (!deepEqual(report?.criticalActions, document.metrics?.criticalActions)) {
      errors.push(`${label}.criticalActions do not match the manifest metrics.`);
    }
  }
  if (name === 'interaction') {
    if (!deepEqual(report?.runtime, document.runtime)) errors.push(`${label}.runtime does not match the manifest.`);
    if (!deepEqual(report?.requiredCriticalActions, document.requiredCriticalActions)) {
      errors.push(`${label}.requiredCriticalActions do not match the manifest.`);
    }
  }
}

function validateOwnerLedger(ownerLedger, errors) {
  if (!Array.isArray(ownerLedger)) return;
  const capabilities = new Set();
  for (const owner of ownerLedger) {
    if (capabilities.has(owner.capability)) errors.push(`ownerLedger capability ${owner.capability} must be unique.`);
    capabilities.add(owner.capability);
    if ((owner.subscriptions > 0 || owner.writes > 0) && owner.mounted !== 1) {
      errors.push(`ownerLedger capability ${owner.capability} cannot subscribe or write while unmounted.`);
    }
  }
  if (!ownerLedger.some(owner => owner.mounted === 1)) errors.push('ownerLedger must contain at least one mounted owner.');
}

function validateRuntimeAudit(document, errors) {
  const runtime = document.runtime ?? {};
  for (const field of ['consoleErrors', 'pageErrors', 'failedRequests', 'httpErrors', 'unexpectedWrites']) {
    if ((runtime[field]?.length ?? 0) > 0) errors.push(`${field} must be empty for accepted evidence.`);
  }
  const expected = [...(runtime.expectedHttpErrors ?? [])].sort();
  const observed = [...(runtime.observedExpectedHttpErrors ?? [])].sort();
  if (!deepEqual(expected, observed)) errors.push('Expected HTTP errors must be observed exactly.');

  const requests = runtime.requests ?? [];
  const allowedWrites = runtime.allowedWrites ?? [];
  const approvedWrites = new Set(approvedWritesByScene[document.scene] ?? []);
  const invalidAllowances = allowedWrites.filter(write => !approvedWrites.has(write));
  if (invalidAllowances.length > 0) errors.push(`Unapproved write allowances: ${invalidAllowances.join(', ')}.`);
  const writes = requests.filter(request => !/^(?:GET|HEAD) /.test(request));
  const unusedAllowances = allowedWrites.filter(write => !writes.includes(write));
  if (unusedAllowances.length > 0) errors.push(`Unused write allowances: ${unusedAllowances.join(', ')}.`);
  const unexpectedWrites = writes.filter(write => !allowedWrites.includes(write));
  if (unexpectedWrites.length > 0) errors.push(`Unexpected writes: ${unexpectedWrites.join(', ')}.`);
  if (!deepEqual(runtime.unexpectedWrites ?? [], unexpectedWrites)) {
    errors.push('runtime.unexpectedWrites does not match the independently computed write audit.');
  }
}

function validateCriticalActions(required, actions, errors, label) {
  if (!Array.isArray(required) || required.length === 0 || !Array.isArray(actions)) return;
  for (const selector of required) {
    const matches = actions.filter(action => action.selector === selector);
    if (matches.length !== 1) {
      errors.push(`${label} critical action ${selector} must appear exactly once.`);
      continue;
    }
    const [action] = matches;
    if (action.truncated || !action.inViewport || !action.reachable || !action.enabled || !action.unobscured) {
      errors.push(`${label} critical action ${selector} must be visible and operable.`);
    }
  }
}

function readPngDimensions(buffer) {
  try {
    return inspectPngBuffer(buffer);
  } catch {
    return null;
  }
}

function validateReferencedPath(path, manifestPath, errors, label) {
  const absolute = resolveReferencedPath(path, manifestPath);
  if (!absolute) {
    errors.push(`${label} path must stay inside the evidence directory: ${path}.`);
    return;
  }
  if (!existsSync(absolute) || !statSync(absolute).isFile()) errors.push(`${label} does not exist: ${path}.`);
}

function resolveReferencedPath(path, manifestPath) {
  if (typeof path !== 'string' || !path || isAbsolute(path)) return null;
  const evidenceRoot = resolve(manifestPath ? dirname(manifestPath) : process.cwd());
  const absolute = resolve(evidenceRoot, path);
  const relativePath = relative(evidenceRoot, absolute);
  if (relativePath.startsWith('..') || isAbsolute(relativePath)) return null;
  return absolute;
}

export function validateBlindReview(document) {
  const errors = validateAgainstSchema(document, readJson(schemaPaths.review));
  const requiredReviewers = document.scope === 'FINAL' ? 3 : 2;
  const requiredGroups = document.scope === 'FINAL' ? 42 : document.scope === 'PHASE' ? 12 : 4;
  if ((document.reviewers?.length ?? 0) < requiredReviewers) errors.push(`${document.scope} review requires at least ${requiredReviewers} reviewers.`);
  const groupCount = document.groups?.length ?? 0;
  if (document.scope === 'FINAL' && groupCount !== 42) errors.push('FINAL review requires exactly 42 groups.');
  else if (groupCount < requiredGroups) errors.push(`${document.scope} review requires at least ${requiredGroups} groups.`);

  const pairIds = new Set();
  if (document.scope === 'FINAL') {
    for (const scene of finalScenes) {
      const sceneGroups = (document.groups ?? []).filter(group => group.scene === scene);
      if (sceneGroups.length !== 3) errors.push(`FINAL review requires exactly 3 groups for ${scene}.`);
      const variants = sceneGroups.map(group => group.variant);
      for (const variant of finalVariants) {
        if (variants.filter(candidate => candidate === variant).length !== 1) {
          errors.push(`FINAL review requires exactly one ${variant} group for ${scene}.`);
        }
      }
    }
  }

  let passedGroups = 0;
  const stageScores = Object.fromEntries(reviewDimensions.map(dimension => [dimension, []]));
  for (const [index, group] of (document.groups ?? []).entries()) {
    if (pairIds.has(group.pairId)) errors.push(`groups[${index}].pairId must be unique.`);
    pairIds.add(group.pairId);
    if (document.scope === 'FINAL' && group.pairId !== `${group.scene}-${group.variant}`) {
      errors.push(`groups[${index}].pairId must bind to ${group.scene}-${group.variant}.`);
    }
    const reviewers = new Set(document.reviewers ?? []);
    const voteReviewers = new Set(group.votes?.map(vote => vote.reviewer));
    if (voteReviewers.size !== group.votes?.length) errors.push(`groups[${index}] contains duplicate reviewer votes.`);
    if (voteReviewers.size !== reviewers.size || [...reviewers].some(reviewer => !voteReviewers.has(reviewer))) errors.push(`groups[${index}] must contain one vote from every reviewer.`);
    const finalVotes = group.votes?.filter(vote => vote.preference === document.finalSide).length ?? 0;
    const requiredFinalVotes = Math.ceil((2 / 3) * (group.votes?.length ?? 0));
    const gateScores = Object.fromEntries(reviewDimensions.map(dimension => [dimension, []]));
    for (const [voteIndex, vote] of (group.votes ?? []).entries()) {
      if (!reviewers.has(vote.reviewer)) errors.push(`groups[${index}].votes[${voteIndex}] references an unknown reviewer.`);
      if (vote.preference === 'no-difference' && (!vote.leftScores || !vote.rightScores)) errors.push(`groups[${index}].votes[${voteIndex}] requires leftScores and rightScores for no-difference.`);
      const effectiveScores = vote.preference === 'no-difference'
        ? lowerScores(vote.leftScores, vote.rightScores)
        : vote.scores;
      if (effectiveScores) {
        const values = Object.values(effectiveScores);
        if (values.some(value => value < 3)) errors.push(`groups[${index}].votes[${voteIndex}] contains a score below 3.`);
        for (const dimension of reviewDimensions) {
          if (Number.isFinite(effectiveScores[dimension])) {
            gateScores[dimension].push(effectiveScores[dimension]);
            stageScores[dimension].push(effectiveScores[dimension]);
          }
        }
      }
    }
    const dimensionsPass = reviewDimensions.every(dimension => median(gateScores[dimension]) >= 4);
    if (finalVotes >= requiredFinalVotes && dimensionsPass) passedGroups += 1;
  }
  const requiredPassRate = document.scope === 'FINAL' ? 0.85 : document.scope === 'PHASE' ? 0.8 : 1;
  const requiredPassedGroups = Math.ceil(requiredPassRate * (document.groups?.length ?? 0));
  if (passedGroups < requiredPassedGroups) errors.push(`Only ${passedGroups}/${document.groups?.length ?? 0} groups passed; ${requiredPassedGroups} are required.`);
  for (const dimension of reviewDimensions) {
    if (median(stageScores[dimension]) < 4) errors.push(`${dimension} review score median must be at least 4.`);
  }
  return errors;
}

function lowerScores(left = {}, right = {}) {
  const result = {};
  for (const key of new Set([...Object.keys(left), ...Object.keys(right)])) result[key] = Math.min(left[key] ?? 0, right[key] ?? 0);
  return result;
}

function median(values) {
  if (!values.length) return 0;
  const sorted = [...values].sort((left, right) => left - right);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

export function validateMotionInventory(document, options = {}) {
  const errors = validateAgainstSchema(document, readJson(schemaPaths.motion));
  const ids = new Set();
  for (const [index, item] of (document.items ?? []).entries()) {
    if (ids.has(item.motionId)) errors.push(`items[${index}].motionId must be unique.`);
    ids.add(item.motionId);
    for (const property of item.properties ?? []) {
      if (forbiddenMotionProperties.test(property) || !permittedMotionProperties.has(property)) errors.push(`${item.motionId} animates forbidden property ${property}.`);
    }
    if (item.durationMs > 200 && !(item.durationMs === 800 && /(?:spinner|progress|loading)/i.test(item.target))) errors.push(`${item.motionId} exceeds the 200ms interaction budget.`);
    if (![100, 140, 180, 200, 800].includes(item.durationMs)) errors.push(`${item.motionId} must use an existing motion duration token.`);
    if (/(?:index|timestamp|random|date\.now|object)/i.test(item.stableKey)) errors.push(`${item.motionId} uses an unstable key source.`);
    if (forbiddenMotionTarget.test(item.target) && item.properties.some(property => property === 'transform' || property === 'opacity')) errors.push(`${item.motionId} animates a real-time or geometry-sensitive forbidden target.`);
    if (item.risk === 'FORBIDDEN_ZONE') errors.push(`${item.motionId} remains in a forbidden motion zone.`);
  }
  if (options.sourceRoot) {
    const snapshot = computeMotionSourceSnapshot(options.sourceRoot);
    if (document.sourceHash !== snapshot.hash) errors.push('Motion inventory sourceHash is stale for the current source tree.');
    if (!deepEqual(document.sourceFiles, snapshot.files)) errors.push('Motion inventory sourceFiles do not match the current motion-bearing source set.');
    const referencedFiles = new Set((document.items ?? []).flatMap(item => item.sourceRefs ?? []));
    for (const file of snapshot.files) {
      if (!referencedFiles.has(file)) errors.push(`Motion-bearing source ${file} is not assigned to an inventory item.`);
    }
    for (const file of referencedFiles) {
      if (!snapshot.files.includes(file)) errors.push(`Motion inventory source reference is missing or no longer motion-bearing: ${file}.`);
    }
    if (options.repositoryRoot) {
      const actualHead = git(options.repositoryRoot, ['rev-parse', 'HEAD']).trim().toLowerCase();
      if (document.generatedFrom !== actualHead) errors.push('Motion inventory generatedFrom does not match current HEAD.');
    }
    errors.push(...scanMotionSource(options.sourceRoot));
  }
  return errors;
}

export function computeMotionSourceSnapshot(sourceRoot) {
  const files = [];
  walkFiles(sourceRoot, path => {
    if (!/\.(?:css|vue)$/.test(path)) return;
    const content = readFileSync(path, 'utf8');
    if (!/(?:\btransition(?:-property)?\s*:|\banimation(?:-name)?\s*:|<Transition(?:Group)?\b)/i.test(content)) return;
    files.push(relative(sourceRoot, path).replaceAll('\\', '/'));
  });
  files.sort((left, right) => left.localeCompare(right, 'en'));
  const hash = createHash('sha256');
  for (const file of files) {
    hash.update(`${file}\0`);
    hash.update(readFileSync(resolve(sourceRoot, file)));
  }
  return Object.freeze({ hash: hash.digest('hex'), files: Object.freeze(files) });
}

export function scanMotionSource(sourceRoot) {
  const errors = [];
  walkFiles(sourceRoot, path => {
    if (!/\.(?:css|vue)$/.test(path)) return;
    const content = readFileSync(path, 'utf8');
    const code = stripComments(content);
    if (/transition\s*:\s*all\b/i.test(code)) errors.push(`${relative(sourceRoot, path)} contains transition: all.`);
    for (const declaration of code.matchAll(/transition(?:-property)?\s*:\s*([^;}\r\n]+)/gi)) {
      const properties = declaration[1].split(',').map(item => item.trim().split(/\s+/, 1)[0]);
      for (const property of properties) {
        if (forbiddenMotionProperties.test(property)) errors.push(`${relative(sourceRoot, path)} transitions forbidden layout property ${property}.`);
      }
    }
    for (const declaration of code.matchAll(/animation\s*:\s*([^;}\r\n]+)/gi)) {
      if (!/\binfinite\b/i.test(declaration[1])) continue;
      const context = code.slice(Math.max(0, declaration.index - 240), declaration.index);
      if (!/(?:spinner|loading|busy)/i.test(context)) errors.push(`${relative(sourceRoot, path)} contains a non-loading infinite animation.`);
    }
    for (const declaration of code.matchAll(/animation-iteration-count\s*:\s*([^;}\r\n]+)/gi)) {
      if (!/\binfinite\b/i.test(declaration[1])) continue;
      const ruleStart = code.lastIndexOf('{', declaration.index);
      const selectorStart = Math.max(code.lastIndexOf('}', ruleStart - 1), code.lastIndexOf(';', ruleStart - 1));
      const context = code.slice(Math.max(0, selectorStart + 1), declaration.index);
      if (!/(?:spinner|loading|busy)/i.test(context)) errors.push(`${relative(sourceRoot, path)} contains a non-loading infinite animation.`);
    }
    for (const transitionGroup of code.matchAll(/<TransitionGroup\b[\s\S]*?<\/TransitionGroup>/gi)) {
      if (/:key\s*=\s*"[^"]*(?:index|Date\.now|random)[^"]*"/i.test(transitionGroup[0])) {
        errors.push(`${relative(sourceRoot, path)} uses an unstable TransitionGroup key.`);
      }
    }
    const addedListeners = collectListenerSignatures(code, 'addEventListener');
    const removedListeners = collectListenerSignatures(code, 'removeEventListener');
    for (const signature of addedListeners) {
      if (!removedListeners.has(signature)) errors.push(`${relative(sourceRoot, path)} adds listener ${signature} without a matching removal path.`);
    }
    const intervalHandles = collectIntervalHandles(code);
    const clearedIntervals = new Set([...code.matchAll(/clearInterval\s*\(\s*([\w$.]+)/g)].map(match => match[1]));
    for (const handle of intervalHandles) {
      if (!clearedIntervals.has(handle)) errors.push(`${relative(sourceRoot, path)} starts interval ${handle} without a matching cleanup path.`);
    }
    if (/letter-spacing\s*:\s*-[^;}\r\n]+/i.test(code)) errors.push(`${relative(sourceRoot, path)} contains negative letter-spacing.`);
    if (/font-size\s*:\s*(?:clamp\([^;}\r\n]*(?:vw|cqw)|[^;}\r\n]*(?:vw|cqw))/i.test(code)) errors.push(`${relative(sourceRoot, path)} scales font size with the viewport.`);
  });
  return errors;
}

function stripComments(content) {
  return content.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1');
}

function collectListenerSignatures(content, method) {
  const signatures = new Set();
  const expression = new RegExp(`([\\w$.]+)\\.${method}\\s*\\(\\s*(['\"])([^'\"]+)\\2\\s*,\\s*([\\w$]+)`, 'g');
  for (const match of content.matchAll(expression)) signatures.add(`${match[1]}.${match[3]}:${match[4]}`);
  return signatures;
}

function collectIntervalHandles(content) {
  const handles = new Set();
  for (const match of content.matchAll(/(?:const|let|var)\s+([\w$]+)\s*=\s*(?:window\.)?setInterval\s*\(/g)) handles.add(match[1]);
  for (const match of content.matchAll(/(this\.[\w$]+)\s*=\s*(?:window\.)?setInterval\s*\(/g)) handles.add(match[1]);
  if (/(?:^|[^=\w$.])(?:window\.)?setInterval\s*\(/m.test(content) && handles.size === 0) handles.add('<unassigned>');
  return handles;
}

function walkFiles(root, visitor) {
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const path = join(root, entry.name);
    if (entry.isDirectory()) walkFiles(path, visitor);
    else if (entry.isFile()) visitor(path);
  }
}

export function validateBrowserSession(document, options = {}) {
  const errors = validateAgainstSchema(document, readJson(schemaPaths.browser));
  const normalized = resolve(document.fixtureRoot ?? '').replaceAll('\\', '/').toLowerCase();
  if (!normalized.endsWith('/.tmp/studio-ui-next/r2/browser-fixture')) errors.push('Browser fixture root must remain under .tmp/studio-ui-next/r2/browser-fixture.');
  const repositoryRoot = options.repositoryRoot ?? findRepositoryRoot();
  if (document.fixtureHash !== computeFixtureHash(repositoryRoot)) errors.push('Browser fixtureHash does not match the current shared fixture content.');
  return errors;
}

function printResult(label, errors) {
  if (errors.length > 0) {
    console.error(`${label} FAIL (${errors.length})`);
    errors.forEach(error => console.error(`- ${error}`));
    process.exitCode = 1;
    return;
  }
  console.log(`${label} PASS`);
}

async function runCli() {
  const [mode, path, ...flags] = process.argv.slice(2);
  if (!mode) return;
  const repositoryRoot = findRepositoryRoot();
  if (mode === 'content-hash') {
    console.log(computeCandidateContentHash(repositoryRoot));
    return;
  }
  if (mode === 'fixture-hash') {
    console.log(computeFixtureHash(repositoryRoot));
    return;
  }
  if (mode === 'motion-source') {
    console.log(JSON.stringify(computeMotionSourceSnapshot(resolve(repositoryRoot, 'ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src')), null, 2));
    return;
  }
  if (!path) throw new Error(`Usage: node ${fileURLToPath(import.meta.url)} <manifest|review|motion|browser|content-hash|fixture-hash> <path>.`);
  const absolute = resolve(path);
  const document = readJson(absolute);
  if (mode === 'manifest') printResult('R2 evidence manifest', validateEvidenceManifest(document, { repositoryRoot, manifestPath: absolute, formal: flags.includes('--formal') }));
  else if (mode === 'review') printResult('R2 blind review', validateBlindReview(document));
  else if (mode === 'motion') printResult('R2 motion inventory', validateMotionInventory(document, {
    sourceRoot: flags.includes('--scan-source') ? resolve(repositoryRoot, 'ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src') : null,
    repositoryRoot
  }));
  else if (mode === 'browser') printResult('R2 browser session', validateBrowserSession(document));
  else throw new Error(`Unknown R2 validator mode: ${mode}.`);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  runCli().catch(error => {
    console.error(error instanceof Error ? error.stack ?? error.message : String(error));
    process.exitCode = 1;
  });
}
