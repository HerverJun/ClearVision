import { createHash } from 'node:crypto';
import { readFile, stat } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const compact1920 = Object.freeze({ width: 1920, height: 1080, density: 'compact' });
const comfortable1920 = Object.freeze({ width: 1920, height: 1080, density: 'comfortable' });
const compact1600 = Object.freeze({ width: 1600, height: 1000, density: 'compact' });
const compact1350 = Object.freeze({ width: 1350, height: 704, density: 'compact' });
const comfortable1350 = Object.freeze({ width: 1350, height: 704, density: 'comfortable' });

export const f04DesignHandoffPlaywrightTitles = Object.freeze([
  'F04 G3C completes create reconcile, open, rename, delete reconcile and tombstone journey',
  'G3 Inspector follows empty, node, multi-node and connection selection from Canvas',
  'G4 Preview and ImageCanvas render artifacts, probe pixels and commit ROI once with undo redo',
  'G4 Preview exposes structured, empty, business failure, safety block, network failure and cancellation states',
  'F04 design handoff captures a deterministic complex flow without static showcase data',
  'Workspace splitters preserve bounds, Preview recovery and layout preferences across re-entry',
  'Prompt 3 refines Operator Rail and populated Inspector across width and long-Chinese states',
  'Prompt 3 Inspector explains disabled parameters without exposing internal metadata terms',
  'Prompt 3 Preview preserves image, result, ROI, empty and error hierarchy on a short comfortable viewport',
  'Workspace Shell fits'
]);

function scene(id, scenario, profile, coverage) {
  return Object.freeze({ id, scenario, ...profile, coverage: Object.freeze(coverage) });
}

export const f04DesignHandoffAutomatedScenes = Object.freeze([
  scene('no-project-open', 'projects-empty', compact1600, ['product-shell', 'no-project']),
  scene('empty-flow', 'workspace-empty', compact1600, ['workspace', 'empty-flow']),
  scene('layout-1920-compact', 'workspace-prompt3-layout-default-compact', compact1920, ['layout', 'compact']),
  scene('layout-1920-comfortable', 'workspace-prompt3-layout-default-comfortable', comfortable1920, ['layout', 'comfortable']),
  scene('layout-1350-compact', 'workspace-prompt3-layout-default-compact', compact1350, ['layout', 'short-viewport']),
  scene('layout-1350-comfortable', 'workspace-prompt3-layout-default-comfortable', comfortable1350, ['layout', 'short-viewport']),
  scene('operator-search-drag', 'workspace-prompt3-operator-search-drag', compact1920, ['operator-rail', 'drag']),
  scene('node-selected-success', 'workspace-node-selected-success', compact1920, ['node-selection', 'execution-success']),
  scene('multi-node-selected', 'workspace-multi-node-selected', compact1920, ['multi-selection']),
  scene('connection-selected', 'workspace-connection-selected', compact1920, ['connection-selection']),
  scene('complex-flow', 'workspace-complex-flow-100-150', compact1920, ['complex-flow', 'minimap']),
  scene('inspector-296-long-zh', 'workspace-prompt3-inspector-default-long-zh', compact1920, ['inspector', 'long-chinese']),
  scene('inspector-248-long-zh', 'workspace-prompt3-inspector-min-long-zh', compact1920, ['inspector', 'minimum-width']),
  scene('inspector-420-validation', 'workspace-prompt3-inspector-max-validation', compact1920, ['inspector', 'maximum-width', 'validation-error']),
  scene('inspector-disabled', 'workspace-prompt3-inspector-disabled', compact1920, ['inspector', 'disabled-parameter']),
  scene('preview-height-160', 'workspace-preview-min', compact1920, ['preview', 'minimum-height']),
  scene('preview-height-420', 'workspace-preview-max', compact1920, ['preview', 'maximum-height']),
  scene('preview-collapsed', 'workspace-preview-collapsed', compact1920, ['preview', 'collapsed']),
  scene('preview-restored', 'workspace-preview-restored', compact1920, ['preview', 'restored']),
  scene('preview-structured-success', 'workspace-preview-structured-success', compact1920, ['preview', 'success']),
  scene('preview-no-output', 'workspace-preview-no-output', compact1920, ['preview', 'no-image']),
  scene('preview-business-failure', 'workspace-preview-business-failure', compact1920, ['preview', 'business-failure']),
  scene('preview-safety-blocked', 'workspace-preview-safety-blocked', compact1920, ['preview', 'safety-blocked']),
  scene('preview-network-failure', 'workspace-preview-network-failure', compact1920, ['preview', 'network-failure']),
  scene('preview-loading', 'workspace-preview-loading', compact1920, ['preview', 'loading']),
  scene('preview-cancelled', 'workspace-preview-cancelled', compact1920, ['preview', 'cancelled']),
  scene('image-probe-locked', 'workspace-image-probe-locked', compact1920, ['image', 'pixel-probe']),
  scene('roi-editing', 'workspace-roi-editing', compact1920, ['image', 'roi']),
  scene('preview-short-success', 'workspace-prompt3-preview-success-1350-comfortable', comfortable1350, ['preview', 'short-viewport']),
  scene('preview-short-roi', 'workspace-prompt3-preview-roi-editing-1350-comfortable', comfortable1350, ['roi', 'short-viewport']),
  scene('preview-short-empty', 'workspace-prompt3-preview-empty-1350-comfortable', comfortable1350, ['preview', 'empty']),
  scene('preview-short-error', 'workspace-prompt3-preview-error-1350-comfortable', comfortable1350, ['preview', 'error'])
]);

export const f04DesignHandoffDeferredStates = Object.freeze([
  Object.freeze({ id: 'operator-flyout-expanded', reason: 'Studio UI Next currently has a single OperatorRail list and category select; no Flyout component is mounted.' }),
  Object.freeze({ id: 'final-decision-unconfigured-valid-invalid', reason: 'The canonical Project contract is preserved, but the Next visible editor/entry is not implemented.' }),
  Object.freeze({ id: 'global-variables-entry', reason: 'GlobalVariables remain in the Project contract and save payload; the Next visible capability owner/entry is not implemented.' }),
  Object.freeze({ id: 'canvas-node-running-success-failed-business-ng', reason: 'The canonical canvas can draw execution status, but the Next FlowCanvas owner does not currently bind authoritative operator execution state; business NG is a decision outcome, not a generic node execution status.' }),
  Object.freeze({ id: 'port-compatible-incompatible-visual', reason: 'Connection compatibility is contract-tested, but a stable product screenshot scene must be added with the Stitch canvas treatment.' })
]);

export const f04DesignHandoffPlaywrightGrep = f04DesignHandoffPlaywrightTitles
  .map(title => title.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
  .join('|');

export function validateF04DesignHandoffCatalog() {
  const ids = new Set();
  const evidenceKeys = new Set();
  for (const item of f04DesignHandoffAutomatedScenes) {
    if (ids.has(item.id)) throw new Error(`Duplicate F04 design handoff scene id: ${item.id}`);
    ids.add(item.id);
    const key = `${item.scenario}:${item.width}x${item.height}:${item.density}`;
    if (evidenceKeys.has(key)) throw new Error(`Duplicate F04 design handoff evidence key: ${key}`);
    evidenceKeys.add(key);
  }
  for (const required of ['product-shell', 'empty-flow', 'compact', 'comfortable', 'long-chinese', 'minimum-width', 'maximum-width', 'complex-flow', 'connection-selection', 'loading', 'safety-blocked', 'pixel-probe', 'roi']) {
    if (!f04DesignHandoffAutomatedScenes.some(item => item.coverage.includes(required))) {
      throw new Error(`F04 design handoff catalog is missing required coverage: ${required}`);
    }
  }
  return true;
}

function evidenceStem(item) {
  return `${item.scenario}-${item.width}x${item.height}-dpr-1`;
}

export async function validateF04DesignHandoffEvidence(directory, sourceSha) {
  if (!/^[0-9a-f]{40}$/i.test(sourceSha)) throw new Error('sourceSha must be a 40-character commit SHA.');
  validateF04DesignHandoffCatalog();
  const root = resolve(directory);
  const checks = [];
  for (const item of f04DesignHandoffAutomatedScenes) {
    const stem = evidenceStem(item);
    const pngPath = resolve(root, `${stem}.png`);
    const jsonPath = resolve(root, `${stem}.json`);
    const [png, metadataText] = await Promise.all([readFile(pngPath), readFile(jsonPath, 'utf8')]);
    const metadata = JSON.parse(metadataText);
    const pngStats = await stat(pngPath);
    const errors = [];
    if (metadata.sourceSha !== sourceSha.toLowerCase()) errors.push('source-sha');
    if (metadata.scenario !== item.scenario) errors.push('scenario');
    if (metadata.viewport?.width !== item.width || metadata.viewport?.height !== item.height) errors.push('viewport');
    if (metadata.projection?.density !== item.density) errors.push('density');
    if (Number(metadata.projection?.horizontalOverflow ?? 1) > 1) errors.push('horizontal-overflow');
    if ((metadata.runtimeErrors?.consoleErrors?.length ?? 0) > 0 || (metadata.runtimeErrors?.pageErrors?.length ?? 0) > 0) errors.push('runtime-errors');
    if (metadata.screenshot?.bytes !== pngStats.size) errors.push('screenshot-size');
    if (metadata.screenshot?.sha256 !== createHash('sha256').update(png).digest('hex').toUpperCase()) errors.push('screenshot-hash');
    checks.push(Object.freeze({ id: item.id, passed: errors.length === 0, errors: Object.freeze(errors) }));
  }
  const failed = checks.filter(item => !item.passed);
  if (failed.length > 0) throw new Error(`F04 design handoff evidence validation failed: ${JSON.stringify(failed)}`);
  return Object.freeze({ status: 'PASS', sourceSha: sourceSha.toLowerCase(), directory: root, scenes: checks.length, checks: Object.freeze(checks) });
}

async function runCli() {
  const [command, directory, sourceSha] = process.argv.slice(2);
  if (command === '--grep') {
    process.stdout.write(`${f04DesignHandoffPlaywrightGrep}\n`);
    return;
  }
  if (command === '--list') {
    process.stdout.write(`${JSON.stringify({ automated: f04DesignHandoffAutomatedScenes, deferred: f04DesignHandoffDeferredStates }, null, 2)}\n`);
    return;
  }
  if (command === '--validate' && directory && sourceSha) {
    const result = await validateF04DesignHandoffEvidence(directory, sourceSha);
    process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
    return;
  }
  throw new Error('Usage: --grep | --list | --validate <evidence-directory> <source-sha>');
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  await runCli();
}
