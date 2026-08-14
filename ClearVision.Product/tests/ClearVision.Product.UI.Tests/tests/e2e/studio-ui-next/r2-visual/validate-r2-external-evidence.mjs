import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export function validateNative125Evidence(document) {
  const errors = [];
  const observations = [];
  collectObjects(document, value => {
    const dpi = value?.nativeWindow?.dpi ?? value?.nativeDpi ?? value?.nativeWindowDpi;
    if (typeof dpi === 'number') observations.push({ dpi, type: value.DPI_TYPE ?? value.dpiType ?? value.screenshotDpiType ?? null });
  });
  if (observations.length === 0) errors.push('No native window DPI observations were found.');
  for (const [index, observation] of observations.entries()) {
    if (observation.dpi !== 120) errors.push(`Native DPI observation ${index} is ${observation.dpi}; 120 is required.`);
    if (observation.type && observation.type !== 'NATIVE_WINDOW_DPI_OBSERVED') errors.push(`Native DPI observation ${index} uses ${observation.type}; NATIVE_WINDOW_DPI_OBSERVED is required.`);
  }
  const forceScaleClaims = collectValuesByKey(document, /(?:forceDeviceScaleFactor|forceScale|browserDprOnly)/i);
  if (forceScaleClaims.some(Boolean) && observations.every(observation => observation.dpi !== 120)) errors.push('Browser force scale or DPR cannot satisfy the native 125% gate.');
  return errors;
}

export function validateIndependentNoNodeEvidence(document) {
  const errors = [];
  const cleanMachine = document?.cleanMachineWithoutNode;
  if (!cleanMachine || cleanMachine.status !== 'PASS') errors.push('cleanMachineWithoutNode.status must be PASS.');
  if (cleanMachine?.nodeInstalled !== false) errors.push('The independent target must record nodeInstalled=false.');
  if (cleanMachine?.sameMachineAsEvidenceDriver !== false) errors.push('The no-Node target must be independent from the evidence driver.');
  if ((cleanMachine?.productProcessTree?.nodeDescendantCount ?? -1) !== 0) errors.push('The tested product process tree must contain zero Node descendants.');
  if (cleanMachine?.runtimeKind !== 'PUBLISHED_RELEASE') errors.push('The independent target must exercise a published Release runtime.');
  return errors;
}

function collectObjects(value, visitor) {
  if (!value || typeof value !== 'object') return;
  visitor(value);
  for (const child of Object.values(value)) collectObjects(child, visitor);
}

function collectValuesByKey(value, pattern, result = []) {
  if (!value || typeof value !== 'object') return result;
  for (const [key, child] of Object.entries(value)) {
    if (pattern.test(key)) result.push(child);
    collectValuesByKey(child, pattern, result);
  }
  return result;
}

function runCli() {
  const [mode, path] = process.argv.slice(2);
  if (!mode || !path) throw new Error('Usage: validate-r2-external-evidence.mjs <dpi-125|no-node-target> <json-path>.');
  const document = JSON.parse(readFileSync(resolve(path), 'utf8'));
  const errors = mode === 'dpi-125'
    ? validateNative125Evidence(document)
    : mode === 'no-node-target'
      ? validateIndependentNoNodeEvidence(document)
      : [`Unknown mode: ${mode}.`];
  if (errors.length) {
    console.error(`R2 external ${mode} FAIL (${errors.length})`);
    errors.forEach(error => console.error(`- ${error}`));
    process.exitCode = 1;
  } else {
    console.log(`R2 external ${mode} PASS`);
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    runCli();
  } catch (error) {
    console.error(error instanceof Error ? error.stack ?? error.message : String(error));
    process.exitCode = 1;
  }
}
